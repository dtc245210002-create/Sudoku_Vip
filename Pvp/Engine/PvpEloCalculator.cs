using System;

namespace sudokuvip.Pvp.Engine
{
    public static class PvpEloCalculator
    {
        public const int KFactor = 32;
        public const int MinRating = 100;

        public static (int p1Change, int p2Change, int p1New, int p2New) Calculate(int p1Rating, int p2Rating, bool p1Won, bool isDraw = false)
        {
            double expected1 = 1.0 / (1.0 + Math.Pow(10, (p2Rating - p1Rating) / 400.0));
            double expected2 = 1.0 / (1.0 + Math.Pow(10, (p1Rating - p2Rating) / 400.0));

            double score1 = isDraw ? 0.5 : (p1Won ? 1.0 : 0.0);
            double score2 = isDraw ? 0.5 : (p1Won ? 0.0 : 1.0);

            int change1 = (int)Math.Round(KFactor * (score1 - expected1));
            int change2 = (int)Math.Round(KFactor * (score2 - expected2));

            // Ensure winning gets at least +1 elo and losing loses at least -1 elo (unless at min)
            if (!isDraw)
            {
                if (p1Won && change1 <= 0) change1 = 1;
                if (!p1Won && change2 <= 0) change2 = 1;
            }

            int new1 = Math.Max(MinRating, p1Rating + change1);
            int new2 = Math.Max(MinRating, p2Rating + change2);

            return (change1, change2, new1, new2);
        }
    }
}
