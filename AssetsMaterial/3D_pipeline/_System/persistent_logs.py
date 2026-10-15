"""Append-only task diagnostics, outside all pipeline scratch directories."""
import functools
import inspect
import json
import os
import subprocess
import sys
import threading
import traceback
from collections import deque
from datetime import datetime
from pathlib import Path


class TaskLog:
    def __init__(self, system, task):
        self.path = Path(system) / 'Logs' / (task + '.log')
        self.asset = '-'
        self.stage = 'startup'
        self.lock = threading.RLock()

    def write(self, message, stream='event', **fields):
        record = dict(timestamp=datetime.now().astimezone().isoformat(),
                      pid=os.getpid(), asset=self.asset, stage=self.stage,
                      stream=stream, message=str(message))
        record.update(fields)
        with self.lock:
            self.path.parent.mkdir(parents=True, exist_ok=True)
            with self.path.open('a', encoding='utf-8') as handle:
                handle.write(json.dumps(record, ensure_ascii=False) + '\n')
                handle.flush()

    def scope(self, stage, asset_arg=None):
        def decorate(fn):
            @functools.wraps(fn)
            def wrapped(*args, **kwargs):
                previous = self.asset, self.stage
                if asset_arg:
                    value = inspect.signature(fn).bind(*args, **kwargs).arguments.get(asset_arg)
                    self.asset = Path(value).name if value is not None else '-'
                self.stage = stage
                self.write('START')
                try:
                    result = fn(*args, **kwargs)
                    self.write('END')
                    return result
                except BaseException as exc:
                    self.write(traceback.format_exc(), exception=type(exc).__name__,
                               exit_code=getattr(exc, 'returncode', None))
                    raise
                finally:
                    self.asset, self.stage = previous
            return wrapped
        return decorate

    def install_console(self):
        task = self
        class Tee:
            def __init__(self, original, channel):
                self.original, self.channel = original, channel
            def write(self, value):
                result = self.original.write(value)
                if value.strip():
                    task.write(value.rstrip('\n'), self.channel)
                return result
            def flush(self):
                self.original.flush()
            def __getattr__(self, key):
                return getattr(self.original, key)
        sys.stdout = Tee(sys.stdout, 'stdout')
        sys.stderr = Tee(sys.stderr, 'stderr')
        self.write('SESSION START')

    def popen(self, cmd, **kwargs):
        # Drain both streams continuously: preserves output even on native crashes.
        self.write('SUBPROCESS START')
        try:
            proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                    text=True, encoding='utf-8', errors='replace', **kwargs)
        except BaseException:
            self.write(traceback.format_exc())
            raise
        asset, stage = self.asset, self.stage
        threads = []
        def drain(pipe, channel):
            with pipe:
                for line in iter(pipe.readline, ''):
                    self.write(line.rstrip('\n'), channel, asset=asset, stage=self.stage)
        for pipe, channel in ((proc.stdout, 'subprocess.stdout'), (proc.stderr, 'subprocess.stderr')):
            thread = threading.Thread(target=drain, args=(pipe, channel), daemon=True)
            thread.start()
            threads.append(thread)
        proc.log_threads = threads
        return proc

    def finish(self, proc):
        for thread in getattr(proc, 'log_threads', []):
            thread.join(timeout=5)
        self.write('SUBPROCESS END', exit_code=proc.poll())

    def run(self, cmd, **kwargs):
        # Preserve capture_output/input semantics used by Claude integrations.
        if kwargs.get('capture_output') or 'input' in kwargs:
            try:
                result = subprocess.run(cmd, **kwargs)
            except subprocess.TimeoutExpired as exc:
                for channel, value in (('subprocess.stdout', exc.stdout), ('subprocess.stderr', exc.stderr)):
                    if value:
                        self.write(value.decode('utf-8', 'replace') if isinstance(value, bytes) else value, channel)
                self.write('SUBPROCESS TIMEOUT', exception=type(exc).__name__, exit_code=124)
                raise
            for channel, value in (('subprocess.stdout', result.stdout), ('subprocess.stderr', result.stderr)):
                if value:
                    self.write(value, channel)
            self.write('SUBPROCESS END', exit_code=result.returncode)
            return result
        proc = self.popen(cmd, **kwargs)
        try:
            rc = proc.wait()
            return subprocess.CompletedProcess(cmd, rc)
        finally:
            if proc.poll() is None:
                proc.kill()
                proc.wait()
            self.finish(proc)

    def preserve_tail(self, path, lines=200):
        path = Path(path)
        if path.is_file():
            with path.open(encoding='utf-8', errors='replace') as handle:
                tail = ''.join(deque(handle, maxlen=lines))
            self.write(tail, 'diagnostic.tail', source=str(path))
