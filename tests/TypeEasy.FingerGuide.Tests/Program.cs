using TypeEasy.Core;

return TestGuard.Run(args, () =>
{
var assertions = 0;
void Assert(bool condition, string message)
{
    assertions++;
    if (!condition) throw new InvalidOperationException(message);
}
for (char c = ' '; c <= '~'; c++) Assert(FingerGuide.Lookup(c) is not null, "ANSI printable key missing: " + c);
foreach (var c in "abcdefghijklmnopqrstuvwxyz")
{
    var lower = FingerGuide.Lookup(c)!;
    var upper = FingerGuide.Lookup(char.ToUpperInvariant(c))!;
    Assert(lower.KeyId == upper.KeyId && lower.Finger == upper.Finger, "letter case maps same physical key");
    Assert(!lower.RequiresShift && upper.RequiresShift, "uppercase requires Shift");
    Assert(upper.ShiftKeyId == (lower.Hand == HandSide.Left ? "RightShift" : "LeftShift"), "opposite Shift for " + c);
}
const string plain = "`1234567890-=[]\\;',./";
const string shifted = "~!@#$%^&*()_+{}|:\"<>?";
Assert(plain.Length == shifted.Length, "symbol map lengths");
for (var i = 0; i < plain.Length; i++)
{
    var ordinary = FingerGuide.Lookup(plain[i])!;
    var symbol = FingerGuide.Lookup(shifted[i])!;
    Assert(ordinary.KeyId == symbol.KeyId && symbol.RequiresShift && !ordinary.RequiresShift, "symbol physical position " + shifted[i]);
    Assert(symbol.ShiftKeyId == (symbol.Hand == HandSide.Left ? "RightShift" : "LeftShift"), "opposite Shift for symbol");
}
Assert(FingerGuide.Lookup('s')!.FingerLabel == "左手无名指", "home-row S label");
Assert(FingerGuide.Lookup('j')!.Hand == HandSide.Right && FingerGuide.Lookup('j')!.Finger == TypingFinger.Index, "J right index");
Assert(FingerGuide.Lookup('6')!.Hand == HandSide.Right && FingerGuide.Lookup('6')!.Finger == TypingFinger.Index, "six uses right index");
Assert(FingerGuide.Lookup(' ')!.Hand == HandSide.Either && FingerGuide.Lookup(' ')!.Finger == TypingFinger.Thumb, "either thumb on space");
foreach (var c in "147") Assert(FingerGuide.Lookup(c, true) is { Hand: HandSide.Right, Finger: TypingFinger.Index }, "keypad index");
foreach (var c in "258") Assert(FingerGuide.Lookup(c, true) is { Hand: HandSide.Right, Finger: TypingFinger.Middle }, "keypad middle");
foreach (var c in "369.") Assert(FingerGuide.Lookup(c, true) is { Hand: HandSide.Right, Finger: TypingFinger.Ring }, "keypad ring");
Assert(FingerGuide.Lookup('0', true) is { Hand: HandSide.Right, Finger: TypingFinger.Thumb }, "keypad zero thumb");
Assert(FingerGuide.Lookup('+', true) is { KeyId: "NumAdd", RequiresShift: false, Finger: TypingFinger.Little }, "keypad plus independent of shifted main plus");
Assert(FingerGuide.Lookup('+') is { KeyId: "=", RequiresShift: true }, "main plus needs Shift");
Assert(FingerGuide.Lookup('\n', true)!.KeyId == "NumEnter" && FingerGuide.Lookup('\n')!.KeyId == "Enter", "separate Enter positions");
Assert(FingerGuide.Lookup('\t') is { KeyId: "Tab", Hand: HandSide.Left, Finger: TypingFinger.Little }, "Tab uses left little");
Assert(FingerGuide.Lookup('\b') is { KeyId: "Backspace", Hand: HandSide.Right, Finger: TypingFinger.Little }, "Backspace uses right little");
Assert(FingerGuide.Lookup('中') is null && FingerGuide.Lookup(null) is null, "unmapped input has no fabricated finger guidance");
Console.WriteLine($"PASS: {assertions} finger-guide assertions.");

return 0;
});

