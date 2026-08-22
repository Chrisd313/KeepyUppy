using KeepyUppy.Sim;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Fans, vents, thermals, gusts and the vacuum cleaner are one system (GDD 6.2). These
    /// tests exist to make adding per-hazard wind code an obvious mistake.
    /// </summary>
    public sealed class AirFieldTests
    {
        [Test]
        public void EmptyField_SamplesZero()
        {
            var field = new AirField();
            Assert.AreEqual(Vector2.zero, field.Sample(new Vector2(5f, 5f)));
        }

        [Test]
        public void OutsideAllZones_SamplesZero()
        {
            var field = new AirField();
            field.Add(new AirZone("a", new Rect(2f, 2f, 4f, 4f), new Vector2(3f, 0f)));

            Assert.AreEqual(Vector2.zero, field.Sample(new Vector2(10f, 10f)));
        }

        [Test]
        public void OverlappingZones_Sum()
        {
            var field = new AirField();
            field.Add(new AirZone("fan", new Rect(0f, 0f, 10f, 10f), new Vector2(4f, 0f)));
            field.Add(new AirZone("thermal", new Rect(5f, 0f, 10f, 10f), new Vector2(0f, 6f)));

            Assert.AreEqual(new Vector2(4f, 0f), field.Sample(new Vector2(2f, 5f)));
            Assert.AreEqual(new Vector2(0f, 6f), field.Sample(new Vector2(12f, 5f)));
            Assert.AreEqual(new Vector2(4f, 6f), field.Sample(new Vector2(7f, 5f)));
        }

        [Test]
        public void OpposingZones_Cancel()
        {
            var field = new AirField();
            field.Add(new AirZone("blow", new Rect(0f, 0f, 10f, 10f), new Vector2(0f, 5f)));
            field.Add(new AirZone("suck", new Rect(0f, 0f, 10f, 10f), new Vector2(0f, -5f)));

            Assert.AreEqual(Vector2.zero, field.Sample(new Vector2(5f, 5f)));
        }

        [Test]
        public void DisabledZone_ContributesNothing()
        {
            var field = new AirField();
            field.Add(new AirZone("armed", new Rect(0f, 0f, 10f, 10f), new Vector2(9f, 9f), false));

            Assert.AreEqual(Vector2.zero, field.Sample(new Vector2(5f, 5f)));

            field.SetEnabled("armed", true);
            Assert.AreEqual(new Vector2(9f, 9f), field.Sample(new Vector2(5f, 5f)));
        }
    }
}
