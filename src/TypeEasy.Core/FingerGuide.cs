namespace TypeEasy.Core;

public enum HandSide { Left, Right, Either }
public enum TypingFinger { Little, Ring, Middle, Index, Thumb }

public sealed record KeyGuidance(string KeyId, string KeyLabel, HandSide Hand, TypingFinger Finger, bool RequiresShift = false)
{
    public string? ShiftKeyId => !RequiresShift ? null : Hand == HandSide.Left ? "RightShift" : "LeftShift";
    public string FingerLabel => (Hand == HandSide.Left ? "左手" : Hand == HandSide.Right ? "右手" : "双手任选")
        + (Finger switch { TypingFinger.Little => "小指", TypingFinger.Ring => "无名指", TypingFinger.Middle => "中指", TypingFinger.Index => "食指", _ => "拇指" });
}

/// <summary>ANSI touch-typing positions, separate from Windows keyboard event handling.</summary>
public static class FingerGuide
{
    private static readonly Dictionary<char, KeyGuidance> MainKeys = BuildMap();

    public static KeyGuidance? Lookup(char? value, bool numericPad = false)
    {
        if (value is null) return null;
        var key = value.Value;
        if (numericPad)
        {
            if (key is >= '0' and <= '9')
            {
                var finger = key switch
                {
                    '0' => TypingFinger.Thumb,
                    '1' or '4' or '7' => TypingFinger.Index,
                    '2' or '5' or '8' => TypingFinger.Middle,
                    _ => TypingFinger.Ring
                };
                return new("Num" + key, key.ToString(), HandSide.Right, finger);
            }
            var num = key switch
            {
                '.' => new KeyGuidance("NumDecimal", ".", HandSide.Right, TypingFinger.Ring),
                '+' => new KeyGuidance("NumAdd", "+", HandSide.Right, TypingFinger.Little),
                '-' => new KeyGuidance("NumSubtract", "−", HandSide.Right, TypingFinger.Little),
                '*' => new KeyGuidance("NumMultiply", "*", HandSide.Right, TypingFinger.Ring),
                '/' => new KeyGuidance("NumDivide", "/", HandSide.Right, TypingFinger.Middle),
                '\r' or '\n' => new KeyGuidance("NumEnter", "Enter", HandSide.Right, TypingFinger.Little),
                _ => null
            };
            if (num is not null) return num;
        }
        return MainKeys.GetValueOrDefault(key);
    }

    private static Dictionary<char, KeyGuidance> BuildMap()
    {
        var map = new Dictionary<char, KeyGuidance>();
        void Add(string characters, HandSide hand, TypingFinger finger)
        {
            foreach (var c in characters)
            {
                var label = char.ToUpperInvariant(c).ToString();
                map[c] = new(label, label, hand, finger);
                if (char.IsLetter(c)) map[char.ToUpperInvariant(c)] = new(label, label, hand, finger, true);
            }
        }
        Add("`1qaz", HandSide.Left, TypingFinger.Little);
        Add("2wsx", HandSide.Left, TypingFinger.Ring);
        Add("3edc", HandSide.Left, TypingFinger.Middle);
        Add("45rtfgvb", HandSide.Left, TypingFinger.Index);
        Add("67yuhjnm", HandSide.Right, TypingFinger.Index);
        Add("8ik,", HandSide.Right, TypingFinger.Middle);
        Add("9ol.", HandSide.Right, TypingFinger.Ring);
        Add("0-p;/'[]=\\", HandSide.Right, TypingFinger.Little);
        const string plain = "`1234567890-=[]\\;',./";
        const string shifted = "~!@#$%^&*()_+{}|:\"<>?";
        for (var i = 0; i < plain.Length; i++)
        {
            var basis = map[plain[i]];
            map[shifted[i]] = basis with { RequiresShift = true };
        }
        map[' '] = new("Space", "空格", HandSide.Either, TypingFinger.Thumb);
        map['\t'] = new("Tab", "Tab", HandSide.Left, TypingFinger.Little);
        map['\r'] = map['\n'] = new("Enter", "Enter", HandSide.Right, TypingFinger.Little);
        map['\b'] = new("Backspace", "Backspace", HandSide.Right, TypingFinger.Little);
        return map;
    }
}
