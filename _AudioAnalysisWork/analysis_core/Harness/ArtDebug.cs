using System;
using System.Linq;
namespace Harness
{
    public static class ArtDebug
    {
        public static void Run(string name, int sr)
        {
            var c = SynthTests.Cases().First(x => x.name.StartsWith(name));
            var b = new SignalBuilder(sr, c.seconds); c.build(b);
            var att = b.truth.Select(t => t.time).ToList();
            var hf = StimulusCheck.UnintendedTransients(b.x, sr, att);
            var bd = StimulusCheck.BoundaryArtefacts(b.x, sr, b.soundEnds, att);
            Console.WriteLine("HF: " + string.Join(" ", hf.Select(t => t.ToString("F3"))));
            Console.WriteLine("boundary: " + string.Join(" ", bd.Select(t => t.ToString("F3"))));
            Console.WriteLine("ends: " + string.Join(" ", b.soundEnds.Take(30).Select(t => t.ToString("F3"))));
        }
    }
}
