using System.Threading.Tasks;

namespace OcuNet;

internal interface IMouseController
{
    Point GetCurrentPosition();

    Task Move(int x, int y, MouseSpeed speed);

    Task SingleClick(int x, int y, MouseSpeed speed);

    Task DoubleClick(int x, int y, MouseSpeed speed);

    Task TripleClick(int x, int y, MouseSpeed speed);

    Task RightClick(int x, int y, MouseSpeed speed);

    Task DragFrom(int x, int y, MouseSpeed speed);

    Task DropTo(int x, int y, MouseSpeed speed);

    Task WheelUp();

    Task WheelDown();

    /// <summary>
    /// Scrolls down by a custom wheel delta (not necessarily a full WHEEL_DELTA=120 click). Delta-aware
    /// apps (browsers, modern UIs) scroll proportionally, which allows precise control of the scroll
    /// speed during scroll screenshots. The send itself must not add pacing delays — the caller controls
    /// the cadence.
    /// </summary>
    Task WheelDown(int delta);
}
