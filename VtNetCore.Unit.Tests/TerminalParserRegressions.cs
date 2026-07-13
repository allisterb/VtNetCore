using System.Text;

using Xunit;

using VtNetCore.VirtualTerminal;
using VtNetCore.XTermParser;

namespace ConnectionNode.Tests.UnitTests
{
    // Regressions found while validating full-screen / alternate-screen programs (vim, less, top) in Jumbee.Console's
    // TerminalEmulator. Both were parser bugs in XTermParser/XTermSequenceReader.cs that leaked control-sequence bytes
    // as visible text.
    public class TerminalParserRegressions
    {
        private const char ESC = (char)0x1b;

        // Renders a byte stream and returns the trimmed text of screen row 0.
        private static string RenderRow0(string stream)
        {
            var controller = new VirtualTerminalController();
            controller.ResizeView(40, 3);
            new DataConsumer(controller).Push(Encoding.UTF8.GetBytes(stream));
            var rows = controller.ViewPort.GetPageSpans(controller.ViewPort.TopRow, 3);
            var sb = new StringBuilder();
            foreach (var span in rows[0].Spans) sb.Append(span.Text);
            return sb.ToString().TrimEnd();
        }

        [Fact]
        public void CsiWithPercentIntermediate_DoesNotLeakFinalByte()
        {
            // A CSI with a '%' intermediate byte (0x25) — e.g. vim's startup `CSI 0 % m` — must consume the whole
            // sequence. Only $ " ' and space were recognized as intermediates, so '%' was taken as the FINAL byte and
            // the real final byte ('m') leaked as literal text (vim's first line rendered as "mMNEMOSYNE").
            Assert.Equal("MNEM", RenderRow0(ESC + "[0%mMNEM"));
        }

        [Fact]
        public void CsiWithSpaceIntermediate_StillWorks()
        {
            // The previously-recognized intermediates must keep working — e.g. `CSI 1 SP q` (DECSCUSR cursor shape).
            Assert.Equal("HELLO", RenderRow0(ESC + "[1 qHELLO"));
        }

        [Fact]
        public void OscTerminatedByBareEsc_DoesNotLeakNextSequence()
        {
            // An OSC terminated by a BARE ESC (the start of the next sequence) is an implicit terminator: the ESC must
            // begin a new sequence, not be swallowed. vim sends back-to-back colour queries; an earlier fix threw here
            // and desynced, leaking the aborted query's bytes as text ("11;?...").
            Assert.Equal("ABCD", RenderRow0(ESC + "]10;?" + ESC + "]11;?" + ESC + "[94mAB" + ESC + "[mCD"));
        }

        [Fact]
        public void OscTerminatedByProperSt_StillWorks()
        {
            // ESC '\' (7-bit ST) remains a valid OSC terminator, and text after it renders.
            Assert.Equal("XYZ", RenderRow0(ESC + "]11;rgb:00/00/00" + ESC + "\\" + "XYZ"));
        }
    }
}
