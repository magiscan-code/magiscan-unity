using Magiscan.Editor.Qr;
using NUnit.Framework;

namespace Magiscan.Tests
{
    public class QrCodeTests
    {
        [Test]
        public void EncodesShortText_ToVersion1()
        {
            var qr = QrCode.EncodeText("HELLO", QrEcc.Medium);
            Assert.AreEqual(1, qr.Version);
            Assert.AreEqual(21, qr.Size);                 // size == version * 4 + 17
            Assert.AreEqual(qr.Version * 4 + 17, qr.Size);
            Assert.IsTrue(qr.Mask >= 0 && qr.Mask <= 7);
        }

        [Test]
        public void EncodesLinkCode_FitsLargerVersion()
        {
            var qr = QrCode.EncodeText("552HqUZIaUP4AKKTMGgGTrLFSHE7rkQq", QrEcc.Medium);
            Assert.GreaterOrEqual(qr.Version, 2);
            Assert.AreEqual(qr.Version * 4 + 17, qr.Size);
        }

        [Test]
        public void GetModule_OutOfBounds_ReturnsFalse()
        {
            var qr = QrCode.EncodeText("HELLO", QrEcc.Medium);
            Assert.IsFalse(qr.GetModule(-1, 0));
            Assert.IsFalse(qr.GetModule(0, -1));
            Assert.IsFalse(qr.GetModule(qr.Size, 0));
            Assert.IsFalse(qr.GetModule(0, qr.Size));
        }

        [Test]
        public void FinderPattern_TopLeftCornerIsDark()
        {
            // The top-left finder pattern starts with a dark module at (0,0).
            var qr = QrCode.EncodeText("HELLO", QrEcc.Medium);
            Assert.IsTrue(qr.GetModule(0, 0));
        }
    }
}
