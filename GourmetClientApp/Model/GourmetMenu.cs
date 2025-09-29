using System;

namespace GourmetClientApp.Model;

public record GourmetMenu(
    DateTime Day,
    GourmetMenuCategory Category,
    string MenuId,
    string MenuName,
    string Description,
    char[] Allergens,
    bool IsAvailable)
{
    /// <summary>
    /// Compares whether this instance is equal to another <see cref="GourmetMenu"/> instance.
    /// Two menus are considered equal if their <see cref="MenuId"/> and <see cref="Day"/> properties are equal.
    /// This is because the menu id is only unique within one day, but menus on different days can have the same menu id.
    /// </summary>
    /// <param name="other">The other instance.</param>
    /// <returns>True if this instance is equal to the other instance, otherwise false.</returns>
    public virtual bool Equals(GourmetMenu? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Day.Equals(other.Day) && MenuId == other.MenuId;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Day, MenuId);
    }
}