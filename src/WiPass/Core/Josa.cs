namespace WiPass.Core
{
    /// <summary>Korean particles chosen by the final consonant of the preceding word.</summary>
    internal static class Josa
    {
        /// <summary>"로" or "으로": 왕북초로, 이리부송초로, 본관으로, 서울로(ㄹ받침은 "로").</summary>
        public static string Ro(string word)
        {
            int jong = FinalConsonant(word);
            if (jong < 0) return "(으)로";
            return jong == 0 || jong == 8 ? "로" : "으로"; // 8 = ㄹ
        }

        /// <summary>Word followed by 로/으로.</summary>
        public static string WithRo(string word)
        {
            return word + Ro(word);
        }

        /// <summary>
        /// Index of the final consonant (0 = none, 8 = ㄹ), or -1 when it cannot be told
        /// (e.g. a word ending in a Latin letter).
        /// </summary>
        private static int FinalConsonant(string word)
        {
            if (string.IsNullOrEmpty(word)) return -1;
            string w = word.TrimEnd(' ', ')', ']', '"', '\'', '.');
            if (w.Length == 0) return -1;
            char c = w[w.Length - 1];
            if (c >= 0xAC00 && c <= 0xD7A3) return (c - 0xAC00) % 28;
            // Digits read in Sino-Korean: 영 일 이 삼 사 오 육 칠 팔 구
            switch (c)
            {
                case '0': return 21; // 영 (ㅇ)
                case '1': return 8;  // 일 (ㄹ)
                case '3': return 16; // 삼 (ㅁ)
                case '6': return 1;  // 육 (ㄱ)
                case '7': return 8;  // 칠 (ㄹ)
                case '8': return 8;  // 팔 (ㄹ)
                case '2':
                case '4':
                case '5':
                case '9': return 0;
            }
            return -1;
        }
    }
}
