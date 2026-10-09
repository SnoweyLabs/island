namespace Island.Core;

/// <summary>
/// One damped spring (mass 1), advanced in fixed steps of 1/120 s driven by elapsed time.
/// The frame rate therefore never changes the path: asked for the position at one moment,
/// the spring gives the same answer whether frames arrived every 1/30 s, every 1/144 s or
/// irregularly, and after a stall the animation is where it would have been.
///
/// Immutable: every advance returns a new value. <see cref="Value"/> and <see cref="Velocity"/>
/// are the state at the last completed step; <see cref="Drawn"/> is the value for the moment
/// in time, interpolated between that step and the next, which is always already computed.
/// A change of target takes effect from the step after the one already computed, so the drawn
/// value never jumps when the target changes.
///
/// One step is: velocity += (-stiffness * (value - target) - damping * velocity) * dt;
/// value += velocity * dt, with dt = 1/120 s (exactly the step the approved preview takes on a
/// 60 Hz screen).
/// </summary>
public readonly record struct Spring
{
    /// <summary>The fixed step, in seconds.</summary>
    public const double StepSeconds = 1.0 / LookConstants.SpringStepsPerSecond;

    /// <summary>
    /// A stall longer than this is not replayed step by step. The spring has long since settled
    /// by then (it decays by a factor of about e^50 in this time), so nothing visible is lost.
    /// </summary>
    private const double MaxCatchUpSeconds = 5;

    /// <summary>Slack that keeps floating-point error from losing or doubling a step.</summary>
    private const double Epsilon = 1e-12;

    private readonly double _nextValue;
    private readonly double _nextVelocity;
    private readonly double _leftover;

    public Spring(double value, double velocity, double target)
        : this(value, velocity, target, 0, 0, Next(value, velocity, target))
    {
    }

    private Spring(double value, double velocity, double target, long steps, double leftover, (double Value, double Velocity) next)
    {
        Value = value;
        Velocity = velocity;
        Target = target;
        Steps = steps;
        _leftover = leftover;
        _nextValue = next.Value;
        _nextVelocity = next.Velocity;
    }

    /// <summary>Position at the last completed step.</summary>
    public double Value { get; }

    /// <summary>Velocity at the last completed step.</summary>
    public double Velocity { get; }

    public double Target { get; }

    /// <summary>How many steps this spring has taken since it was created or snapped.</summary>
    public long Steps { get; }

    /// <summary>Time since the last completed step, in seconds (always less than one step).</summary>
    public double Leftover => _leftover;

    /// <summary>The value to draw now: between the last step and the next one.</summary>
    public double Drawn => Value + (_nextValue - Value) * (_leftover / StepSeconds);

    /// <summary>The velocity to use now, interpolated the same way as <see cref="Drawn"/>.</summary>
    public double DrawnVelocity => Velocity + (_nextVelocity - Velocity) * (_leftover / StepSeconds);

    public static Spring At(double value) => new(value, 0, value);

    /// <summary>Same motion, new target. The step already computed is kept, so nothing jumps.</summary>
    public Spring WithTarget(double target) =>
        new(Value, Velocity, target, Steps, _leftover, (_nextValue, _nextVelocity));

    /// <summary>Teleports to a value and stops. Only for an island that is not on screen.</summary>
    public Spring Snap(double value) => new(value, 0, value);

    /// <summary>Advances by the time that has elapsed since the last call, in seconds.</summary>
    public Spring Frame(double elapsedSeconds)
    {
        var elapsed = double.IsNaN(elapsedSeconds) ? 0 : Math.Clamp(elapsedSeconds, 0, MaxCatchUpSeconds);
        var time = elapsed + _leftover;
        var s = this;
        var value = s.Value;
        var velocity = s.Velocity;
        var next = (Value: s._nextValue, Velocity: s._nextVelocity);
        var steps = s.Steps;

        while (time >= StepSeconds - Epsilon)
        {
            value = next.Value;
            velocity = next.Velocity;
            next = Next(value, velocity, Target);
            time -= StepSeconds;
            steps++;
        }

        return new Spring(value, velocity, Target, steps, Math.Max(0, time), next);
    }

    /// <summary>One plain step from the given state.</summary>
    public static (double Value, double Velocity) Next(double value, double velocity, double target)
    {
        var v = velocity + (-LookConstants.SpringStiffness * (value - target) - LookConstants.SpringDamping * velocity) * StepSeconds;
        return (value + v * StepSeconds, v);
    }
}
