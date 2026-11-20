namespace ReController.Core.Mapping;

/// <summary>Pure stick / trigger shaping functions shared by the engine and the UI previews.</summary>
public static class AnalogProcessing
{
    public static (float X, float Y) ShapeStick(float x, float y, StickSettings s)
    {
        if (s.InvertX) x = -x;
        if (s.InvertY) y = -y;

        double mag = Math.Sqrt((x * x) + (y * y));
        if (mag < 1e-6) return (0, 0);

        double dz = Math.Clamp(s.Deadzone, 0, 0.9);
        double outer = Math.Clamp(s.OuterDeadzone, dz + 0.05, 1.0);
        if (mag <= dz) return (0, 0);

        double t = Math.Clamp((mag - dz) / (outer - dz), 0, 1);
        double curve = Math.Clamp(s.Curve, 0.2, 5);
        t = Math.Pow(t, curve);
        double ad = Math.Clamp(s.AntiDeadzone, 0, 0.9);
        t = ad + ((1 - ad) * t);

        double scale = t / mag;
        float ox = (float)Math.Clamp(x * scale, -1, 1);
        float oy = (float)Math.Clamp(y * scale, -1, 1);
        return (ox, oy);
    }

    public static float ShapeTrigger(float raw, TriggerSettings s)
    {
        double dz = Math.Clamp(s.Deadzone, 0, 0.9);
        if (raw <= dz) return 0;
        if (s.HairTrigger) return 1;
        double max = Math.Clamp(s.MaxZone, dz + 0.05, 1);
        return (float)Math.Clamp((raw - dz) / (max - dz), 0, 1);
    }
}
