using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PolyglotNotebooks.Variables;

#pragma warning disable MSTEST0037

namespace PolyglotNotebooks.Test
{
    [TestClass]
    public class VariableInfoTests
    {
        // ── Defaults ─────────────────────────────────────────────────

        [TestMethod]
        public void AllProperties_DefaultToExpectedValues()
        {
            var info = new VariableInfo();

            Assert.AreEqual(string.Empty, info.Name);
            Assert.AreEqual(string.Empty, info.TypeName);
            Assert.AreEqual(string.Empty, info.Value);
            Assert.AreEqual(string.Empty, info.KernelName);
        }

        // ── PropertyChanged: Name ────────────────────────────────────

        [TestMethod]
        public void Name_WhenSet_FiresPropertyChanged()
        {
            var info = new VariableInfo();
            var changed = new List<string>();
            info.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

            info.Name = "myVar";

            CollectionAssert.Contains(changed, nameof(VariableInfo.Name));
        }

        [TestMethod]
        public void Name_WhenSetToSameValue_StillFiresPropertyChanged()
        {
            // The implementation always fires — no same-value guard
            var info = new VariableInfo();
            info.Name = "x";

            var changed = new List<string>();
            info.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

            info.Name = "x";

            CollectionAssert.Contains(changed, nameof(VariableInfo.Name),
                "Implementation fires PropertyChanged even when value unchanged");
        }

        // ── PropertyChanged: TypeName ────────────────────────────────

        [TestMethod]
        public void TypeName_WhenSet_FiresPropertyChanged()
        {
            var info = new VariableInfo();
            var changed = new List<string>();
            info.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

            info.TypeName = "System.Int32";

            CollectionAssert.Contains(changed, nameof(VariableInfo.TypeName));
        }

        // ── PropertyChanged: Value ───────────────────────────────────

        [TestMethod]
        public void Value_WhenSet_FiresPropertyChanged()
        {
            var info = new VariableInfo();
            var changed = new List<string>();
            info.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

            info.Value = "42";

            CollectionAssert.Contains(changed, nameof(VariableInfo.Value));
        }

        // ── PropertyChanged: KernelName ──────────────────────────────

        [TestMethod]
        public void KernelName_WhenSet_FiresPropertyChanged()
        {
            var info = new VariableInfo();
            var changed = new List<string>();
            info.PropertyChanged += (s, e) => changed.Add(e.PropertyName!);

            info.KernelName = "csharp";

            CollectionAssert.Contains(changed, nameof(VariableInfo.KernelName));
        }

        // ── Values are stored correctly ──────────────────────────────

        [TestMethod]
        public void Properties_WhenSet_ReturnNewValues()
        {
            var info = new VariableInfo
            {
                Name = "counter",
                TypeName = "System.Int64",
                Value = "99",
                KernelName = "fsharp",
            };

            Assert.AreEqual("counter", info.Name);
            Assert.AreEqual("System.Int64", info.TypeName);
            Assert.AreEqual("99", info.Value);
            Assert.AreEqual("fsharp", info.KernelName);
        }
    }
}
