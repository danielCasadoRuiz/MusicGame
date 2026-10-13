"""Protection tests: no Quad Remesher or Blender is launched by these tests."""
import json
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

SYSTEM = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SYSTEM))
sys.path.insert(0, str(SYSTEM / 'FittingAndRigging'))
import incremental_outputs as inc
import retopo_worker as retopo
import controller

TARGETS = [{'label': l, 'quads': q} for l, q in
           [('ULTRA',50000),('HIGH',30000),('MIDHIGH',20000),('MEDIUM',10000),('LOW',5000)]]

def snapshot(root):
    return {p.relative_to(root).as_posix():p.read_bytes() for p in root.rglob('*') if p.is_file()}

class IncrementalTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.output = self.root/'Output'
        self.output.mkdir()
        self.asset = self.output/'shoe'
        self.asset.mkdir()

    def seed_retopo(self, archived=True):
        for t in TARGETS:
            if t['label'] != 'MIDHIGH':
                (self.asset/retopo.lod_name('shoe', t['label'], t['quads'])).write_bytes(t['label'].encode())
        rawdir = self.asset/'preProcess' if archived else self.root/'Raw3D/shoe'
        rawdir.mkdir(parents=True)
        raw = rawdir/'original_before_rename.glb' if archived else rawdir/'shoe.glb'
        raw.write_bytes(b'original RAW')
        (self.asset/'Rigged').mkdir()
        (self.asset/'Rigged/protected.fbx').write_bytes(b'unchanged rig')
        return raw

    def fake_engine(self, command, **kwargs):
        raw = Path(command[2])
        directory = Path(command[command.index('--output')+1])
        (directory/(raw.stem+'_HIGH.glb')).write_bytes(b'new MIDHIGH')
        return SimpleNamespace(returncode=0)

    def test_archived_renamed_raw_only_generates_missing_lod(self):
        raw = self.seed_retopo()
        before = snapshot(self.output)
        with patch.multiple(retopo,ROOT=self.root,OUTPUT=self.output,WORK_ROOT=self.root/'_W'), patch.object(retopo.subprocess,'run',side_effect=self.fake_engine) as run:
            jobs = retopo.archived_packages(TARGETS)
            self.assertEqual(jobs,[(None,raw,'shoe',True)])
            done, errors = retopo.process_package(None,raw,TARGETS,set(),'shoe',True)
            self.assertTrue(done)
            self.assertFalse(errors)
            self.assertEqual(run.call_count,1)
            self.assertEqual(run.call_args.args[0][run.call_args.args[0].index('--quads')+1],'20000')
            self.assertEqual(retopo.archived_packages(TARGETS),[])
            done, errors = retopo.process_package(None,raw,TARGETS,set(),'shoe',True)
            self.assertFalse(done)
            self.assertEqual(run.call_count,1)
        after = snapshot(self.output)
        for name, data in before.items(): self.assertEqual(after[name],data)
        self.assertEqual(set(after)-set(before),{'shoe/shoe_MIDHIGH_20k.glb'})

    def test_retopo_failure_leaves_all_outputs_and_archived_raw(self):
        raw = self.seed_retopo()
        before = snapshot(self.output)
        with patch.multiple(retopo,ROOT=self.root,OUTPUT=self.output,WORK_ROOT=self.root/'_W'), patch.object(retopo.subprocess,'run',return_value=SimpleNamespace(returncode=1)):
            _,errors = retopo.process_package(None,raw,TARGETS,set(),'shoe',True)
        self.assertTrue(errors)
        self.assertEqual(snapshot(self.output),before)
        self.assertTrue(raw.exists())

    def test_main_completes_output_with_empty_raw3d(self):
        self.seed_retopo()
        rawdir=self.root/'Raw3D';rawdir.mkdir()
        before=snapshot(self.output)
        with patch.multiple(retopo,ROOT=self.root,OUTPUT=self.output,RAW=rawdir,WORK_ROOT=self.root/'_W'), patch.object(retopo,'load_targets',return_value=TARGETS), patch.object(retopo,'preflight'), patch.object(retopo.subprocess,'run',side_effect=self.fake_engine) as run, patch.object(sys,'argv',['retopo_worker.py','--once']):
            self.assertEqual(retopo.main(),0)
            self.assertEqual(run.call_count,1)
            self.assertEqual(list(rawdir.iterdir()),[])
            self.assertEqual(retopo.main(),0)
            self.assertEqual(run.call_count,1)
        after=snapshot(self.output)
        for name,data in before.items():self.assertEqual(after[name],data)
        self.assertEqual(set(after)-set(before),{'shoe/shoe_MIDHIGH_20k.glb'})

    def test_output_archive_has_priority_over_same_named_raw_queue(self):
        archived=self.seed_retopo()
        rawdir=self.root/'Raw3D';rawdir.mkdir()
        (rawdir/'shoe.glb').write_bytes(b'different queued version')
        with patch.multiple(retopo,OUTPUT=self.output,RAW=rawdir):
            self.assertEqual(retopo.pending_packages(TARGETS),[(None,archived,'shoe',True)])

    def test_first_retopo_archives_raw_and_optional_files_after_success(self):
        package=self.root/'Raw3D/shoe';package.mkdir(parents=True)
        raw=package/'shoe.glb';raw.write_bytes(b'fresh RAW')
        (package/'source.png').write_bytes(b'optional reference')
        with patch.multiple(retopo,ROOT=self.root,OUTPUT=self.output,WORK_ROOT=self.root/'_W'), patch.object(retopo.subprocess,'run',side_effect=self.fake_engine) as run:
            _,errors=retopo.process_package(package,raw,TARGETS,set())
            self.assertFalse(errors)
            self.assertEqual(run.call_count,5)
        self.assertEqual(inc.missing_lods('shoe',self.asset,TARGETS),[])
        self.assertEqual((self.asset/'preProcess/shoe.glb').read_bytes(),b'fresh RAW')
        self.assertEqual((self.asset/'preProcess/source.png').read_bytes(),b'optional reference')
        self.assertFalse(package.exists())

    def test_different_raw_is_not_mixed_with_existing_outputs(self):
        self.seed_retopo()
        rawdir=self.root/'Raw3D/shoe';rawdir.mkdir(parents=True)
        raw=rawdir/'shoe.glb';raw.write_bytes(b'a different RAW')
        before=snapshot(self.output)
        with patch.multiple(retopo,ROOT=self.root,OUTPUT=self.output,WORK_ROOT=self.root/'_W'),patch.object(retopo.subprocess,'run') as run:
            _,errors=retopo.process_package(rawdir,raw,TARGETS,set())
            self.assertTrue(errors)
            run.assert_not_called()
        self.assertEqual(snapshot(self.output),before)
        self.assertTrue(raw.exists())

    def test_legacy_medium_20k_is_recognised(self):
        self.seed_retopo()
        old=self.asset/'shoe_MEDIUM_20k.glb';old.write_bytes(b'old MIDHIGH')
        self.assertEqual(inc.missing_lods('shoe',self.asset,TARGETS),[])
        self.assertEqual(controller.lod_rank(old),2)
        self.assertEqual(controller.lod_output_folder(old),'MID-HIGH')

    def test_changed_asset_prefix_and_lod_folders_are_recognised(self):
        for target in TARGETS:
            if target['label']=='MIDHIGH':continue
            (self.asset/retopo.lod_name('old_asset_name',target['label'],target['quads'])).write_bytes(b'existing')
        self.assertEqual([t['label'] for t in inc.missing_lods('shoe',self.asset,TARGETS)],['MIDHIGH'])
        folder=self.asset/'HIGH-MID';folder.mkdir()
        (folder/'model.glb').write_bytes(b'existing 20k')
        self.assertEqual(inc.missing_lods('shoe',self.asset,TARGETS),[])
        self.assertEqual(controller.lod_rank(folder/'model.glb'),2)
        self.assertEqual(controller.lod_output_folder(folder/'model.glb'),'MID-HIGH')

    def test_archived_and_rigged_glb_do_not_count_as_retopo(self):
        for folder in ('preProcess','Rigged'):
            path=self.asset/folder;path.mkdir()
            for t in TARGETS:(path/retopo.lod_name('shoe',t['label'],t['quads'])).write_bytes(b'not retopo')
        self.assertEqual(len(inc.missing_lods('shoe',self.asset,TARGETS)),5)

    def test_rigging_recognises_existing_fbx_with_old_asset_name(self):
        cfg,avatar,lods,fit=self.seed_rigging()
        old=controller.rigged_fbx_path(self.asset,cfg,lods[0])
        old.rename(old.with_name('old_name_ULTRA_50k_rigged.fbx'))
        self.assertEqual(controller.missing_rigged_lods(self.asset,cfg,lods),[lods[2]])

    def test_publish_collision_never_overwrites_or_adds_other_files(self):
        stage=self.root/'stage';stage.mkdir()
        (stage/'a.glb').write_bytes(b'new')
        (stage/'protected.fbx').write_bytes(b'different')
        final=self.asset/'Rigged';final.mkdir()
        (final/'protected.fbx').write_bytes(b'protected')
        before=snapshot(final)
        with self.assertRaises(RuntimeError):inc.publish_additions(stage,final)
        self.assertEqual(snapshot(final),before)

    def test_exclusive_publish_race_rolls_back_only_new_links(self):
        stage=self.root/'stage';stage.mkdir()
        (stage/'a.png').write_bytes(b'a');(stage/'z.fbx').write_bytes(b'z')
        real_link=inc.os.link
        def race(src,dst):
            if dst.suffix=='.fbx':
                dst.write_bytes(b'other process')
            return real_link(src,dst)
        with patch.object(inc.os,'link',side_effect=race):
            with self.assertRaises(FileExistsError):inc.publish_additions(stage,self.asset)
        self.assertFalse((self.asset/'a.png').exists())
        self.assertEqual((self.asset/'z.fbx').read_bytes(),b'other process')

    def seed_rigging(self):
        lods=[]
        cfg={'rigged_subdir':'Rigged','work_dir':'_work','blender':str(self.root/'blender.exe'),'keep_work_files':False}
        Path(cfg['blender']).write_bytes(b'fake executable')
        avatar=self.root/'avatar.fbx';avatar.write_bytes(b'avatar')
        for t in TARGETS:
            model=self.asset/retopo.lod_name('shoe',t['label'],t['quads'])
            model.write_bytes(t['label'].encode());lods.append(model)
            if t['label']!='MIDHIGH':
                fbx=controller.rigged_fbx_path(self.asset,cfg,model)
                fbx.parent.mkdir(parents=True,exist_ok=True);fbx.write_bytes(b'protected '+t['label'].encode())
                fbx.with_suffix('.blend').write_bytes(b'protected blend')
                (fbx.parent/'Textures').mkdir();(fbx.parent/'Textures/Normal.png').write_bytes(b'protected normal')
        fit={'model':lods[0].name,'model_sha256':controller.sha256_file(lods[0]),'avatar_sha256':controller.sha256_file(avatar),
             'garment_vertices':8,'needs_blendshapes':False,'fit_snapshot':{'garment_matrix_world':[],'pose_basis':{'Root':[]},'symmetry':None}}
        controller.save_json(controller.fit_record_path(self.asset,cfg),fit)
        return cfg,avatar,lods,fit

    def run_fake_rig(self,cfg,avatar,lods,fit,fail=False):
        commands=[]
        proc=SimpleNamespace(poll=lambda:0,wait=lambda **kwargs:0)
        def command(session,cmd,**payload):
            commands.append((cmd,payload))
            if cmd=='commit_manual_fit':return {'result':{'fit_snapshot':fit['fit_snapshot'],'garment_vertices':8}}
            if cmd=='process_lod':
                if fail:raise RuntimeError('simulated worker failure')
                model=Path(payload['model'])
                folder=session/'publish'/controller.lod_output_folder(model)
                folder.mkdir(parents=True)
                (folder/(model.stem+'_rigged.fbx')).write_bytes(b'new rig')
                (folder/(model.stem+'_rigged.blend')).write_bytes(b'new clean blend')
                (folder/'Textures').mkdir();(folder/'Textures/Normal.png').write_bytes(b'new normal')
            return {'result':{}}
        with patch.object(controller.subprocess,'Popen',return_value=proc) as launch,patch.object(controller,'wait_blender_ready',return_value={'fit_reused':True}),patch.object(controller,'send_command',side_effect=command),patch.object(controller,'yes_no',side_effect=AssertionError('unexpected question')):
            controller.process_asset(self.root,cfg,self.asset,lods,avatar)
        return commands,launch.call_count

    def test_rigging_exports_only_missing_and_is_noop_on_second_run(self):
        cfg,avatar,lods,fit=self.seed_rigging()
        before=snapshot(self.output)
        commands,count=self.run_fake_rig(cfg,avatar,lods,fit)
        self.assertEqual(count,1)
        self.assertNotIn('export_current',[c for c,_ in commands])
        models=[p['model'] for c,p in commands if c=='process_lod']
        self.assertEqual(models,[str(lods[2].resolve())])
        after=snapshot(self.output)
        for name,data in before.items():self.assertEqual(after[name],data)
        self.assertEqual(len(set(after)-set(before)),3)
        commands,count=self.run_fake_rig(cfg,avatar,lods,fit)
        self.assertEqual(count,0);self.assertEqual(commands,[])
        self.assertEqual(snapshot(self.output),after)

    def test_rigging_failure_publishes_nothing(self):
        cfg,avatar,lods,fit=self.seed_rigging();before=snapshot(self.output)
        with self.assertRaisesRegex(RuntimeError,'simulated'):self.run_fake_rig(cfg,avatar,lods,fit,True)
        self.assertEqual(snapshot(self.output),before)

    def test_missing_fitting_or_changed_avatar_protect_existing_fbx(self):
        cfg,avatar,lods,fit=self.seed_rigging();before=snapshot(self.output)
        avatar.write_bytes(b'changed avatar')
        with patch.object(controller.subprocess,'Popen') as launch:
            with self.assertRaisesRegex(RuntimeError,'fitting antic'):controller.process_asset(self.root,cfg,self.asset,lods,avatar)
            launch.assert_not_called()
        self.assertEqual(snapshot(self.output),before)
        controller.fit_record_path(self.asset,cfg).unlink()
        before=snapshot(self.output)
        with patch.object(controller.subprocess,'Popen') as launch:
            with self.assertRaisesRegex(RuntimeError,'falta un fit_record'):controller.process_asset(self.root,cfg,self.asset,lods,avatar)
            launch.assert_not_called()
        self.assertEqual(snapshot(self.output),before)

if __name__=='__main__':unittest.main()
