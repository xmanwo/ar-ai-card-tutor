using UnityEngine;

/// <summary>
/// House rules and table configuration. Data lives here so a rule change does not need a code
/// change, and so a user study can run two rule variants side by side.
///
/// Only the first field affects the P0 play check. The rest describe turn flow, which is not
/// implemented yet; they are declared now so the asset layout does not churn later.
/// </summary>
[CreateAssetMenu(fileName = "RuleSet", menuName = "CardTutor/Rule Set")]
public class RuleSet : ScriptableObject
{
    [Header("Play check (P0)")]
    [Tooltip("Official rule: a Wild Draw Four may only be played when the hand holds no card " +
             "of the colour currently in force.")]
    public bool wildDrawFourNeedsNoActiveColour = true;

    [Header("Turn flow (P1 - not enforced yet)")]
    [Tooltip("How many players sit at the table. Two players makes Reverse act as Skip.")]
    public int playerCount = 2;

    public bool drawTwoStacking = true;
    public bool wildDrawFourStacking = true;
    public bool reverseActsAsSkipInTwoPlayer = true;
    public bool mustCallUno = true;
    public bool drawOneOnly = true;
}
