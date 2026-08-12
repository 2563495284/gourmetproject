using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class CandidateItemIconTests
    {
        private static readonly string[] IconBasenames =
        {
            "transfer_extra_targets",
            "flavored_flat",
            "flavored_count_as",
            "active_count_flat_all",
            "empty_active_slot_mult_all",
            "edge_flat",
            "non_edge_mult",
            "gold_per5_flat_all",
            "empty_cell_flat_all",
            "unused_discard_mult_all",
            "unused_discard_flat_all",
            "skip_reward_dish_luck",
            "skip_reward_passive_luck",
            "super_material_spread",
            "count_as_cake",
            "super_fragment_reward",
            "settle_permanent_flat_all",
            "same_base_mult_all",
            "remove_arrow_cookie_2",
            "remove_arrow_cookie_all",
            "randomize_recipe_dishes",
            "discard_dish_flat",
            "heart_capacity",
            "heart_capacity_deluxe",
            "restore_heart",
            "restore_hearts",
            "slot_cost_discount",
        };

        [Test]
        public void CandidateItemIcons_LoadDirectlyAsTransparent512Sprites()
        {
            foreach (string basename in IconBasenames)
            {
                Sprite sprite = Resources.Load<Sprite>($"Sprites/Items/{basename}");

                Assert.That(sprite, Is.Not.Null, basename);
                Assert.That(sprite.texture.width, Is.EqualTo(512), basename);
                Assert.That(sprite.texture.height, Is.EqualTo(512), basename);
                Assert.That(sprite.texture.format, Is.Not.EqualTo(TextureFormat.RGB24), basename);
                Assert.That(sprite.rect.width, Is.EqualTo(512f).Within(0.01f), basename);
                Assert.That(sprite.rect.height, Is.EqualTo(512f).Within(0.01f), basename);
            }
        }
    }
}
