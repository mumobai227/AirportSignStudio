using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace AirportSignStudio
{
    public enum SignColorType
    {
        GroundDirection, GroundLocation, GroundMandatory, GroundStand,
        TaxiDirection, TaxiLocation, TaxiMandatory, StandSign, MaxSpan, HoldingPoint, NoEntry, Aircraft
    }
    public sealed class TypeColorEntry
    {
        public SignColorType Type;
        public ColorSettings Colors = new ColorSettings();
    }
    public sealed class TypeColorSettings
    {
        public List<TypeColorEntry> Entries = new List<TypeColorEntry>();
        public static readonly string[] Names = { "地面 · 方向", "地面 · 位置", "地面 · 强制指令", "地面 · 机位编号",
            "指示牌 · 方向", "指示牌 · 位置", "指示牌 · 强制指令", "机位号码牌", "MAX SPAN", "HP", "NO ENTRY", "机型文字" };
        public ColorSettings Get(SignColorType type)
        {
            TypeColorEntry entry = Entries.FirstOrDefault(e => e.Type == type);
            return entry == null ? new ColorSettings() : entry.Colors;
        }
        public void Set(SignColorType type, ColorSettings colors)
        {
            colors.Validate(); Entries.RemoveAll(e => e.Type == type);
            if (colors.Active) Entries.Add(new TypeColorEntry { Type = type, Colors = colors.Copy() });
            Entries = Entries.OrderBy(e => e.Type).ToList();
        }
        public TypeColorSettings Copy()
        {
            return new TypeColorSettings { Entries = Entries.Select(e => new TypeColorEntry { Type = e.Type, Colors = e.Colors.Copy() }).ToList() };
        }
        public void Validate()
        {
            if (Entries == null || Entries.Count > Names.Length || Entries.Any(e => e == null || !Enum.IsDefined(typeof(SignColorType), e.Type) || e.Colors == null) ||
                Entries.Select(e => e.Type).Distinct().Count() != Entries.Count) throw new ArgumentException("类型配色数据无效。");
            foreach (TypeColorEntry entry in Entries) entry.Colors.Validate();
        }
        public static SignColorType For(Settings settings)
        {
            if (settings.Kind == AssetKind.GroundTaxi) return settings.GroundStyle == "位置" ? SignColorType.GroundLocation : settings.GroundStyle == "强制指令" ? SignColorType.GroundMandatory : SignColorType.GroundDirection;
            if (settings.Kind == AssetKind.GroundStand) return SignColorType.GroundStand;
            if (settings.Kind == AssetKind.StandSign) return SignColorType.StandSign;
            if (settings.Kind == AssetKind.TaxiSign) return Panel(settings.Panels.Count == 0 ? "方向" : settings.Panels[0].Kind);
            return settings.OtherStyle == "HP" ? SignColorType.HoldingPoint : settings.OtherStyle == "NO ENTRY" ? SignColorType.NoEntry : settings.OtherStyle == "机型文字" ? SignColorType.Aircraft : SignColorType.MaxSpan;
        }
        public static SignColorType Panel(string kind) { return kind == "位置" ? SignColorType.TaxiLocation : kind == "强制指令" ? SignColorType.TaxiMandatory : SignColorType.TaxiDirection; }
        public static Settings Sample(SignColorType type)
        {
            Settings s = new Settings { Arrow = "↑" };
            if (type <= SignColorType.GroundMandatory) s.GroundStyle = type == SignColorType.GroundLocation ? "位置" : type == SignColorType.GroundMandatory ? "强制指令" : "方向";
            else if (type == SignColorType.GroundStand) { s.Kind = AssetKind.GroundStand; s.Text = "21"; }
            else if (type <= SignColorType.TaxiMandatory)
            {
                s.Kind = AssetKind.TaxiSign; s.CharacterHeight = 300;
                s.Panels.Add(new SignPanel { Kind = type == SignColorType.TaxiLocation ? "位置" : type == SignColorType.TaxiMandatory ? "强制指令" : "方向", Text = type == SignColorType.TaxiMandatory ? "09" : "A1" });
            }
            else if (type == SignColorType.StandSign) { s.Kind = AssetKind.StandSign; s.CharacterHeight = 600; s.Text = "21"; }
            else
            {
                s.Kind = AssetKind.GroundOther;
                s.OtherStyle = type == SignColorType.HoldingPoint ? "HP" : type == SignColorType.NoEntry ? "NO ENTRY" : type == SignColorType.Aircraft ? "机型文字" : "MAX SPAN";
                s.Text = type == SignColorType.HoldingPoint ? "4" : type == SignColorType.Aircraft ? "A320" : "36";
                if (type == SignColorType.Aircraft) { s.CharacterHeight = 300; s.BlackBackground = false; s.Border = false; }
            }
            return s;
        }
    }
    public sealed class ColorPreset
    {
        public int Version = 2;
        public string Name = "配色";
        public ColorSettings Common = new ColorSettings();
        public TypeColorSettings Types = new TypeColorSettings();
        public BaseColorSettings BaseColors = new BaseColorSettings();
        public ColorPreset Copy() { return new ColorPreset { Version = Version, Name = Name, Common = Common.Copy(), Types = Types.Copy(), BaseColors = BaseColors.Copy() }; }
    }
    public static class ColorPresetStore
    {
        static readonly XmlSerializer Serializer = new XmlSerializer(typeof(ColorPreset));
        public static string Encode(ColorPreset preset)
        {
            Validate(preset); StringBuilder text = new StringBuilder();
            using (XmlWriter writer = XmlWriter.Create(text, new XmlWriterSettings { OmitXmlDeclaration = true, Indent = true })) Serializer.Serialize(writer, preset);
            return text.ToString();
        }
        public static ColorPreset Decode(string text)
        {
            ColorPreset preset;
            try
            {
                using (XmlReader reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 100000 }))
                    preset = (ColorPreset)Serializer.Deserialize(reader);
            }
            catch (InvalidOperationException error) { throw new ArgumentException("配色预设格式无效。", error); }
            catch (XmlException error) { throw new ArgumentException("配色预设格式无效。", error); }
            Validate(preset); return preset;
        }
        static void Validate(ColorPreset preset)
        {
            if (preset == null || preset.Version < 1 || preset.Version > 2 || string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 100 || preset.Common == null || preset.Types == null || preset.BaseColors == null)
                throw new ArgumentException("配色预设无效或版本不受支持。");
            preset.Common.Validate(); preset.Types.Validate(); preset.BaseColors.Validate();
        }
        public static void Save(ColorPreset preset, string path)
        {
            string text = Encode(preset); ProjectStore.AtomicFile(path, temp => File.WriteAllText(temp, text, new UTF8Encoding(false)));
        }
    }
}
