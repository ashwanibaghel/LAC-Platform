import ctypes
import json
import os
import tempfile
import threading
import unittest
from pathlib import Path
from unittest.mock import patch
from worker import atomic_json


def sharing_error():
    error = PermissionError('synthetic Windows sharing failure')
    error.winerror = 5
    return error


class PublicationTests(unittest.TestCase):
    def test_transient_winerror5_retries_same_atomic_temp_then_succeeds(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root)/'refresh.json'
            atomic_json(path, {'status':'Running'})
            original = os.replace
            with patch('worker.os.replace', side_effect=[sharing_error(), None]) as replace, patch('worker.time.sleep') as sleep:
                # The second call deliberately mocked: inspect exact same temp,
                # then exercise a real atomic replacement through a callback.
                replace.side_effect = [sharing_error(), lambda: None]
                calls = []
                def transient(src, dst):
                    calls.append((src,dst))
                    if len(calls)==1: raise sharing_error()
                    return original(src,dst)
                replace.side_effect = transient
                atomic_json(path, {'status':'Completed'})
            self.assertEqual(2,len(calls)); self.assertEqual(calls[0],calls[1])
            self.assertEqual({'status':'Completed'},json.loads(path.read_text()))
            sleep.assert_called_once_with(0.02)
            self.assertEqual([path],list(Path(root).iterdir()))

    def test_permanent_failure_preserves_previous_and_cleans_temp(self):
        with tempfile.TemporaryDirectory() as root:
            path=Path(root)/'refresh.json'; atomic_json(path,{'status':'Running'})
            with patch('worker.os.replace',side_effect=sharing_error()) as replace, patch('worker.time.sleep') as sleep:
                with self.assertRaises(PermissionError): atomic_json(path,{'status':'Completed'})
            self.assertEqual(6,replace.call_count); self.assertEqual(5,sleep.call_count)
            self.assertLess(sum(call.args[0] for call in sleep.call_args_list),1)
            self.assertEqual({'status':'Running'},json.loads(path.read_text()))
            self.assertEqual([path],list(Path(root).iterdir()))

    def test_non_windows_permission_failure_is_not_retried(self):
        with tempfile.TemporaryDirectory() as root, patch('worker.os.replace',side_effect=PermissionError('access denied')) as replace:
            with self.assertRaises(PermissionError): atomic_json(Path(root)/'refresh.json',{})
            self.assertEqual(1,replace.call_count)

    def test_failed_serialization_also_preserves_previous_and_cleans_temp(self):
        with tempfile.TemporaryDirectory() as root:
            path=Path(root)/'refresh.json'; atomic_json(path,{'status':'Completed'})
            with self.assertRaises(TypeError): atomic_json(path,{'bad':object()})
            self.assertEqual({'status':'Completed'},json.loads(path.read_text()))
            self.assertEqual([path],list(Path(root).iterdir()))

    @unittest.skipUnless(os.name=='nt','Windows sharing contract')
    def test_real_windows_reader_without_delete_share_reproduces_then_recovers(self):
        with tempfile.TemporaryDirectory() as root:
            path=Path(root)/'refresh.json'; atomic_json(path,{'status':'Running'})
            api=ctypes.WinDLL('kernel32',use_last_error=True)
            api.CreateFileW.argtypes=[ctypes.c_wchar_p,ctypes.c_uint32,ctypes.c_uint32,ctypes.c_void_p,ctypes.c_uint32,ctypes.c_uint32,ctypes.c_void_p]
            api.CreateFileW.restype=ctypes.c_void_p
            api.CloseHandle.argtypes=[ctypes.c_void_p]
            handle=api.CreateFileW(str(path),0x80000000,1,None,3,0,None)
            self.assertNotEqual(ctypes.c_void_p(-1).value,handle)
            timer=threading.Timer(0.08,lambda:api.CloseHandle(handle)); timer.start()
            try: atomic_json(path,{'status':'Completed'})
            finally: timer.join()
            self.assertEqual('Completed',json.loads(path.read_text())['status'])


if __name__=='__main__': unittest.main()
