namespace DoubleSlitPhysics.Models;

/// <summary>
///     Which slits are open during an evolution. LeftOnly/RightOnly model the
///     post-measurement ("which slit did it go through?") collapsed states.
/// </summary>
public enum SlitMode
{
    Both,
    LeftOnly,
    RightOnly,
}
