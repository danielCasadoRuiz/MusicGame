using NUnit.Framework;
using UnityEngine;
public class PowerDurationTests
{
    [TestCase(0,0f)] [TestCase(1,4f)] [TestCase(2,5f)] [TestCase(5,8f)] [TestCase(9,12f)] [TestCase(30,12f)]
    public void DurationUsesCappedSeconds(int charges,float expected)
    {
        var balance=ScriptableObject.CreateInstance<FightCombatBalanceConfig>();
        try { Assert.AreEqual(expected,CombatResourceUse.PowerSeconds(charges,balance));
              Assert.AreEqual(expected,CombatResourceUse.PowerSeconds(charges,null)); }
        finally { Object.DestroyImmediate(balance); }
    }
    [Test]
    public void DisplayTracksActualDamageBoostAndReturnsToBase()
    {
        var balance=ScriptableObject.CreateInstance<FightCombatBalanceConfig>();
        try {
            var power=new FighterPowerState();
            power.TryActivate(null,4f,balance.PowerMultipliers);
            foreach(var stat in new[]{FightStatId.PunchPower,FightStatId.KickPower}) {
                float shown=FightController.PowerDisplayPoints(16f,stat,power,balance);
                Assert.That(shown,Is.GreaterThan(16f));
                Assert.That(balance.buildStatToDamage.Evaluate(shown),Is.EqualTo(balance.buildStatToDamage.Evaluate(16f)*power.Multipliers.damage).Within(.001f));
            }
            Assert.AreEqual(16f,FightController.PowerDisplayPoints(16f,FightStatId.Agility,power,balance));
            Assert.AreEqual(16f,FightController.PowerDisplayPoints(16f,FightStatId.Resistance,power,balance));
            power.Tick(null,4f);
            Assert.AreEqual(16f,FightController.PowerDisplayPoints(16f,FightStatId.PunchPower,power,balance));
        } finally { Object.DestroyImmediate(balance); }
    }
}
