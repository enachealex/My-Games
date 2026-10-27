// The joystick (StickInput.cs): where the thumb goes down, it stays.

using NUnit.Framework;
using UnityEngine;

namespace Graveway.Tests
{
    public class StickTests
    {
        [Test]
        public void TheBaseStaysWhereTheThumbWentDown()
        {
            var s = new StickInput { enabled = true };
            var down = new Vector2(300, 400);
            s.Finger(1, down);
            Assert.That(s.Active);
            s.Finger(1, down + new Vector2(s.Radius * 4, 0));
            Assert.That(s.Origin, Is.EqualTo(down), "not dragged along");
            Assert.That(s.Knob.x, Is.EqualTo(s.Radius).Within(1e-3f), "the knob waits at the rim");
            Assert.That(s.Move.x, Is.EqualTo(-1).Within(1e-3f), "full right (world -x)");
            s.Finger(1, down - new Vector2(s.Radius * 0.5f, 0));
            Assert.That(s.Origin, Is.EqualTo(down));
            Assert.That(s.Move.x, Is.GreaterThan(0), "and straight back the other way");
            s.Finger(1, null);
            Assert.That(s.Active, Is.False);
        }
    }
}
