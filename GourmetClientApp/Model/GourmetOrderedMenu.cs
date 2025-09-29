using System;

namespace GourmetClientApp.Model;

public record GourmetOrderedMenu(
    DateTime Day,
    string PositionId,
    string EatingCycleId,
    string MenuName,
    bool IsOrderApproved,
    bool IsOrderCancelable)
{
    /// <summary>
    /// Compares whether this instance is equal to another <see cref="GourmetOrderedMenu"/> instance.
    /// Two menus are considered equal if their <see cref="Day"/> and <see cref="MenuName"/> properties are equal.
    /// This is because if a menu is ordered multiple times, then the <see cref="PositionId"/> is different, even if
    /// they are referring to the same menu.
    /// </summary>
    /// <param name="other">The other instance.</param>
    /// <returns>True if this instance is equal to the other instance, otherwise false.</returns>
    public virtual bool Equals(GourmetOrderedMenu? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Day == other.Day && MenuName == other.MenuName;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Day, MenuName);
    }

    /// <summary>
    /// Checks whether this <see cref="GourmetOrderedMenu"/> instance matches a <see cref="GourmetMenu"/> instance.
    /// Since the ordered menu and the actual menu do not share any common identifier, the <see cref="Day"/> and
    /// <see cref="MenuName"/> are used for comparison.
    /// </summary>
    /// <param name="menu">The menu instance to match against.</param>
    /// <returns>True if the ordered menu matches the actual menu, otherwise false.</returns>
    public bool MatchesMenu(GourmetMenu menu)
    {
        return Day == menu.Day && MenuName == menu.MenuName;
    }
}