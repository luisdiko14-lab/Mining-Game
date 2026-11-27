import unittest

import gen_textures


class TexturePaletteTests(unittest.TestCase):
    def test_blackstone_does_not_fall_back_to_stone(self):
        self.assertEqual(gen_textures.base_color("blackstone"), (47, 46, 55))

    def test_redstone_uses_its_ore_palette(self):
        self.assertEqual(gen_textures.base_color("redstone_block"), (157, 54, 48))

    def test_deepslate_ore_keeps_ore_colour(self):
        self.assertEqual(gen_textures.base_color("iron_ore_deepslate"), (180, 133, 108))

    def test_coloured_blocks_use_their_dye(self):
        self.assertEqual(gen_textures.base_color("blue_wool"), (59, 83, 174))
        self.assertEqual(gen_textures.base_color("red_concrete"), (175, 58, 52))

    def test_species_leaves_remain_foliage(self):
        self.assertEqual(gen_textures.base_color("dark_oak_leaves"), (64, 137, 72))
        self.assertEqual(gen_textures.base_color("cherry_leaves"), (105, 157, 91))

    def test_mossy_blocks_keep_the_moss_palette(self):
        self.assertEqual(gen_textures.base_color("mossy_stone_bricks"), (91, 122, 79))


if __name__ == "__main__":
    unittest.main()
