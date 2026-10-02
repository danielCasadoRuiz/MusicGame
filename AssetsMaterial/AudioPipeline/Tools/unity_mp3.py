"""The MP3 that goes into Unity for a rendered master (32-bit float WAV).

Unity's MP3 importer ignores the LAME gapless header, so every MP3 plays with extra leading/trailing
silence. To keep that as small as possible:
  - the master's own silence (below -60 dBFS) is trimmed at both ends (short margins kept),
  - the encode is CBR 192 kbps 44.1 kHz stereo WITHOUT the Xing/LAME info frame (Unity decodes that frame
    as 1152 extra samples of silence); only the codec's own ~25 ms delay remains.
No gain, fade, tempo or pitch change.
"""
import os, subprocess
import numpy as np
from common import *
from render_qa import read_wav

FFMPEG = Path(os.environ.get("LOCALAPPDATA", "")) / "Microsoft/WinGet/Packages/Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe/ffmpeg-9.0.2-full_build/bin/ffmpeg.exe"
BITRATE = "192k"
SILENCE_DBFS = -60.0
LEAD_MARGIN, TAIL_MARGIN = 32, 4410  # samples kept before the first / after the last audible sample (0.7 ms / 100 ms)


def trim_silence(audio):
    level = np.max(np.abs(audio), axis=1)
    loud = np.nonzero(level > 10 ** (SILENCE_DBFS / 20))[0]
    if len(loud) == 0: return audio, 0, 0
    start = max(0, loud[0] - LEAD_MARGIN)
    end = min(len(audio), loud[-1] + 1 + TAIL_MARGIN)
    return audio[start:end], int(start), int(len(audio) - end)


def build(master_wav, dest_mp3):
    audio, rate, ch, _, _ = read_wav(master_wav)
    if rate != 44100 or ch != 2: raise ValueError(f"{master_wav}: expected 44.1 kHz stereo, got {rate}/{ch}")
    trimmed, lead, tail = trim_silence(audio)
    dest_mp3.parent.mkdir(parents=True, exist_ok=True)
    r = subprocess.run([str(FFMPEG), "-v", "error", "-y", "-f", "f32le", "-ar", "44100", "-ac", "2", "-i", "-",
                        "-c:a", "libmp3lame", "-b:a", BITRATE, "-write_xing", "0", "-id3v2_version", "0", str(dest_mp3)],
                       input=np.ascontiguousarray(trimmed, dtype="<f4").tobytes(), capture_output=True)
    if r.returncode != 0: raise RuntimeError(r.stderr.decode(errors="replace"))
    return dict(format=f"MP3 44.1 kHz stereo {BITRATE}bps CBR (LAME, no Xing/LAME info frame)", fromMaster=str(Path(master_wav).relative_to(ROOT).as_posix()),
                masterSamples=len(audio), trimmedLeadSamples=lead, trimmedTailSamples=tail, encodedSamples=len(trimmed),
                durationSeconds=round(len(trimmed) / 44100, 3), bytes=dest_mp3.stat().st_size, sha256=sha256(dest_mp3))
