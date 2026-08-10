using System;
using System.Collections.Generic;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Game.Analytics
{
    public readonly struct ArchetypeVector
    {
        public static readonly ArchetypeVector Mixed = new ArchetypeVector(0d, 0d, 0d, "mixed", 0d);

        public ArchetypeVector(double share0, double share1, double share2, string id, double lead)
        {
            Share0 = share0;
            Share1 = share1;
            Share2 = share2;
            Id = id ?? "mixed";
            Lead = lead;
        }

        public double Share0 { get; }
        public double Share1 { get; }
        public double Share2 { get; }
        public string Id { get; }
        public double Lead { get; }
    }

    public static class ArchetypeService
    {
        public static ArchetypeVector Capture(GameRun run)
        {
            if (run?.RecipeEntries == null || run.RecipeEntries.Count == 0)
            {
                return ArchetypeVector.Mixed;
            }

            double a = 0d;
            double b = 0d;
            double c = 0d;
            int count = 0;
            foreach (RecipeBookSlot slot in run.RecipeEntries)
            {
                DishDef dish = run.Database?.GetDish(slot?.DishId);
                IReadOnlyList<float> weights = dish?.ArchetypeWeights;
                if (!IsValidWeights(weights))
                {
                    return ArchetypeVector.Mixed;
                }

                a += weights[0];
                b += weights[1];
                c += weights[2];
                count++;
            }

            if (count == 0)
            {
                return ArchetypeVector.Mixed;
            }

            a /= count;
            b /= count;
            c /= count;
            return Classify(
                a,
                b,
                c,
                run.Tables?.TbGameBase?.ArchetypePrimaryMinShare ?? 0.45f,
                run.Tables?.TbGameBase?.ArchetypePrimaryMinLead ?? 0.10f);
        }

        public static ArchetypeVector Classify(
            double share0,
            double share1,
            double share2,
            double minimumShare,
            double minimumLead)
        {
            if (!IsFinite(share0) || !IsFinite(share1) || !IsFinite(share2)
                || share0 < 0d || share1 < 0d || share2 < 0d
                || Math.Abs(share0 + share1 + share2 - 1d) > 0.001d)
            {
                return ArchetypeVector.Mixed;
            }

            double[] values = { share0, share1, share2 };
            int first = 0;
            int second = 1;
            if (values[second] > values[first])
            {
                (first, second) = (second, first);
            }

            for (int i = 2; i < values.Length; i++)
            {
                if (values[i] > values[first])
                {
                    second = first;
                    first = i;
                }
                else if (values[i] > values[second])
                {
                    second = i;
                }
            }

            double lead = values[first] - values[second];
            string id = values[first] + 1e-9 >= minimumShare && lead + 1e-9 >= minimumLead
                ? first.ToString()
                : "mixed";
            return new ArchetypeVector(share0, share1, share2, id, lead);
        }

        private static bool IsValidWeights(IReadOnlyList<float> weights)
        {
            if (weights == null || weights.Count != 3)
            {
                return false;
            }

            double sum = 0d;
            for (int i = 0; i < weights.Count; i++)
            {
                double value = weights[i];
                if (!IsFinite(value) || value < 0d)
                {
                    return false;
                }

                sum += value;
            }

            return Math.Abs(sum - 1d) <= 0.001d;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
