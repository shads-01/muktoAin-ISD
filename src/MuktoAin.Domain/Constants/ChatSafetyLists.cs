namespace MuktoAin.Domain.Constants;

// Keyword/phrase lists for ChatSafetyFilter's free C# pre-filter (spec 3.4).
// Kept in Domain alongside Disclaimers so they are editable without touching
// logic. Deliberately conservative: only clear-cut abuse trips the filter;
// vague-but-benign messages pass through and are handled by the intake
// model's missingInfo sharpening questions instead.
public static class ChatSafetyLists
{
    // Requests for help committing crimes / causing harm.
    public static readonly string[] CrimeFacilitation =
    {
        "make a bomb", "build a bomb", "pipe bomb", "make poison", "buy poison",
        "make a weapon", "make a gun", "3d print gun", "silencer",
        "kill someone", "kill him", "kill her", "murder him", "murder her",
        "i will kill", "i will murder",
        "how to poison", "how to stab", "how to shoot someone", "hack into", "hack account",
        "forge a signature", "forged signature", "counterfeit money", "print fake money",
        "bom বানাই", "বোমা বানানো", "বোমা বানাব", "বিষ দেওয়া", "বিষ কিনব",
        "আমি খুন করব", "আমি ওকে খুন করব", "আমি খুন করে দেব", "আমি মেরে ফেলব",
        "খুন করার উপায়", "গুলি করব", "ছুরি মারব", "অ্যাকাউন্ট হ্যাক", "হ্যাক করব",
        "জাল স্বাক্ষর", "জাল টাকা"
    };

    // Threats / false-accusation drafting requests.
    public static readonly string[] FalseAccusation =
    {
        "false complaint against", "false case against", "false allegation against",
        "frame someone", "frame him", "frame her", "fake complaint against",
        "false gd against", "false fir against", "threaten to file",
        "মিথ্যা মামলা", "মিথ্যা অভিযোগ", "মিথ্যা জিডি", "মিথ্যা ফেল করব", "ফাঁসাতে চাই",
        "ফাঁসাতে চাই", "হুমকি দেওয়ার লেখা"
    };

    // Prompt-injection phrasing.
    public static readonly string[] Injection =
    {
        "ignore previous instructions", "ignore all previous", "ignore your instructions",
        "disregard your instructions", "forget your instructions", "your new instructions",
        "system prompt", "reveal your prompt", "you are now dan", "act as dan",
        "developer mode", "jailbreak",
        "পূর্ববর্তী নির্দেশ উপেক্ষা", "আগের নির্দেশ ভুলে", "নতুন নির্দেশ মানো",
        "তোমার নির্দেশনা ভুলে যাও", "সিস্টেম প্রম্পট"
    };

    // Requests to draft legal consequences for the sender's enemies / abuse
    // of the drafting feature itself.
    public static readonly string[] Threats =
    {
        "send a fake legal notice", "fake legal notice", "false police report",
        "মিথ্যা আইনি নোটিশ", "ভুয়া নোটিশ"
    };
}
