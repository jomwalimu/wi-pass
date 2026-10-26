using System.Reflection;

namespace WiPass.Core
{
    internal static class AppInfo
    {
        public const string Name = "WiPass";
        public const string KoreanName = "와이패스";
        public const string Tagline = "같은 이름 학교 와이파이 자동 전환";
        public const string Author = "jomwalimu";
        public const string AuthorNote = "전북특별자치도교육청 소속 · 개인 제작, 교육청 공식 프로그램 아님";
        public const string RepoUrl = "https://github.com/jomwalimu/wi-pass";
        public const string IssuesUrl = "https://github.com/jomwalimu/wi-pass/issues";

        public static string Version
        {
            get
            {
                var attr = (AssemblyInformationalVersionAttribute)System.Attribute.GetCustomAttribute(
                    Assembly.GetExecutingAssembly(), typeof(AssemblyInformationalVersionAttribute));
                string v = attr != null ? attr.InformationalVersion : Assembly.GetExecutingAssembly().GetName().Version.ToString();
                int plus = v.IndexOf('+');
                return plus > 0 ? v.Substring(0, plus) : v;
            }
        }

        public static string DisplayName { get { return KoreanName + " (" + Name + ")"; } }
    }
}
