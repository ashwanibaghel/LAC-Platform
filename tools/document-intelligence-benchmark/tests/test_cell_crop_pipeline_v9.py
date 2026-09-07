import sys, unittest
from pathlib import Path
from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).parents[1]))
from benchmark.cell_crop_pipeline_v9 import PIPELINE_VERSION, cell_identity, normalize_cell_crop, role_alphabet
from benchmark.recognizer_shootout_v9 import semantic, whitespace, classify


class UnifiedCellCropTests(unittest.TestCase):
    def test_same_pixels_produce_same_normalization(self):
        image=Image.new('RGB',(100,40),'white'); ImageDraw.Draw(image).text((25,12),'6//25',fill='black')
        first,meta1=normalize_cell_crop(image,{'x':0,'y':0,'width':100,'height':40})
        second,meta2=normalize_cell_crop(image,{'x':0,'y':0,'width':100,'height':40})
        self.assertEqual(first.tobytes(),second.tobytes()); self.assertEqual(meta1.rawSha256,meta2.rawSha256); self.assertEqual(meta1.pipelineVersion,PIPELINE_VERSION)

    def test_aspect_ratio_is_not_stretched(self):
        wide=Image.new('RGB',(200,40),'white'); tall=Image.new('RGB',(100,40),'white')
        a,_=normalize_cell_crop(wide,{'x':0,'y':0,'width':200,'height':40}); b,_=normalize_cell_crop(tall,{'x':0,'y':0,'width':100,'height':40})
        self.assertEqual(a.height,b.height); self.assertGreater(a.width,b.width)

    def test_edge_rule_removal_does_not_touch_central_ink(self):
        image=Image.new('L',(80,40),'white'); draw=ImageDraw.Draw(image); draw.line((0,1,79,1),fill='black',width=1); draw.rectangle((35,15,45,25),fill='black')
        normalized,meta=normalize_cell_crop(image.convert('RGB'),{'x':0,'y':0,'width':80,'height':40})
        self.assertGreater(meta.borderPixelsSuppressed,0); self.assertLess(normalized.getbbox()[2],normalized.width+1)

    def test_semantic_area_only_normalizes_formatting(self):
        self.assertEqual(semantic('area','4 -- 16'),semantic('area','4-16'))
        self.assertNotEqual(semantic('khasra','6//25'),semantic('khasra','6//26'))
        self.assertEqual(whitespace(' 6//25 '),'6//25')

    def test_role_error_classification(self):
        self.assertEqual(classify('6//25','625','khasra'),'slash lost')
        self.assertEqual(classify('4 -- 16','4 16','area'),'dash lost')
        self.assertEqual(classify('4-16','4-15','area'),'digit substitution')

    def test_identity_is_source_coordinate_not_label(self):
        self.assertEqual(cell_identity(2,1,0),'page:2:row:1:column:0')

    def test_role_specific_alphabets_do_not_mix_identifier_and_area_symbols(self):
        self.assertIn('/',role_alphabet('khasra')); self.assertNotIn('/',role_alphabet('area'))
        self.assertIn('-',role_alphabet('area')); self.assertNotIn('-',role_alphabet('rectangle'))

if __name__=='__main__': unittest.main()
