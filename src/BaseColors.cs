using System;
using System.Drawing;
using System.Text.RegularExpressions;

namespace AirportSignStudio
{
    public sealed class BaseColorSettings
    {
        public string Yellow = "", Black = "", White = "", Red = "";
        public static readonly string[] Names = { "黄色", "黑色", "白色", "红色" };
        public static readonly Color[] Defaults = { SignEngine.Yellow, SignEngine.Black, SignEngine.White, SignEngine.Red };
        public bool Active { get { return Yellow.Length > 0 || Black.Length > 0 || White.Length > 0 || Red.Length > 0; } }
        public BaseColorSettings Copy() { return (BaseColorSettings)MemberwiseClone(); }
        public string Get(int index) { return index == 0 ? Yellow : index == 1 ? Black : index == 2 ? White : Red; }
        public void Set(int index, string value)
        {
            if (index == 0) Yellow = value; else if (index == 1) Black = value; else if (index == 2) White = value; else Red = value;
        }
        public Color? Replacement(Color original)
        {
            for (int i = 0; i < Defaults.Length; i++)
                if (original.ToArgb() == Defaults[i].ToArgb() && Get(i).Length > 0) return ColorTranslator.FromHtml(Get(i));
            return null;
        }
        public void Validate()
        {
            for (int i = 0; i < Defaults.Length; i++)
                if (Get(i) == null || Get(i).Length > 0 && !Regex.IsMatch(Get(i), @"\A#[0-9A-Fa-f]{6}\z")) throw new ArgumentException("颜色请使用 #RRGGBB。");
        }
    }
}
