namespace MuktoAin.Domain.Enums;

public enum AiRequestType
{
    LawIdentification = 0,
    RightsExplanation = 1,
    Drafting = 2,

    // Conversational intake turns (envelope calls + safety-blocked turns).
    // Deliberately NOT RightsExplanation so AiBudgetService's quota count
    // (which sums RightsExplanation AI_LOG rows) stays in control: real
    // intake turns are logged as RightsExplanation to charge quota, while
    // blocked turns log as ChatIntake and stay free.
    ChatIntake = 3
}
