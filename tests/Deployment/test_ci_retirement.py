import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import retire_ci_fixtures as cleanup


class RetirementTests(unittest.TestCase):
    def test_local_invocation_is_rejected(self):
        with patch.dict(os.environ,{'GITHUB_ACTIONS':'false'}),patch.object(cleanup.fixture,'run') as run:
            with self.assertRaises(RuntimeError):cleanup.retire()
            run.assert_not_called()

    def exercise(self, running=False, foreign=False):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory).resolve();folder=root/'.local/recovery-targets/fr-restore-test-ci';folder.mkdir(parents=True)
            owner='a'*48;name=folder.name
            names={'container':[name+'-app'],'volume':[name+'-data'],'network':[name+'-net']}
            (folder/'manifest.json').write_text(json.dumps(dict(owner=owner,names=names)))
            (folder/'compose.json').write_text(json.dumps(dict(name=name,networks={'net':dict(name=name+'-net')})))
            calls=[]
            def run(args):
                calls.append(args)
                if args[1:4]==['container','ls','-a']:return name+'-app'
                if 'inspect' in args:
                    labels={cleanup.fixture.LABEL:'foreign' if foreign else owner}
                    return json.dumps([dict(Config=dict(Labels=labels),Labels=labels,State=dict(Running=running))])
                return ''
            with patch.dict(os.environ,{'GITHUB_ACTIONS':'true'}),patch.object(Path,'cwd',return_value=root),patch.object(cleanup.fixture,'ROOT',root),patch.object(cleanup.fixture,'inventory',return_value={k:set(v) for k,v in names.items()}),patch.object(cleanup.fixture,'run',side_effect=run):
                if running or foreign:
                    with self.assertRaises(RuntimeError):cleanup.retire()
                else:cleanup.retire()
            downs=[c for c in calls if c[-1]=='down']
            self.assertEqual(0 if running or foreign else 1,len(downs))
            self.assertFalse(any('-v' in c or 'prune' in c for c in calls))
            self.assertTrue((folder/'manifest.json').exists())

    def test_only_stopped_owned_project_is_retired_without_volumes(self):self.exercise()
    def test_running_project_is_rejected(self):self.exercise(running=True)
    def test_foreign_owner_is_rejected(self):self.exercise(foreign=True)


if __name__=='__main__':unittest.main()
