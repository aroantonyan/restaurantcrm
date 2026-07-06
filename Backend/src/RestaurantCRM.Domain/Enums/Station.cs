namespace RestaurantCRM.Domain.Enums;

/// <summary>
/// Prep station a menu category's items are routed to on the kitchen display.
/// Category-level routing is the grain mature KDS products (Square, Toast) use:
/// drinks → Bar, everything else → Kitchen. The queue resolves the station
/// through MenuItem → Category at read time, so a re-routed category also
/// re-routes items already sitting in the queue (applied on the next fetch).
/// </summary>
public enum Station
{
    Kitchen,
    Bar
}
