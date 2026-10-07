using LanPE.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LanPE.Tests;

[TestClass]
public class HashUtilTests
{
    [TestMethod]
    public void Sha256_OfKnownContent_Matches()
    {
        string temp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(temp, "abc");
            // sha256("abc")
            const string expected = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
            Assert.AreEqual(expected, HashUtil.Sha256File(temp));
        }
        finally { File.Delete(temp); }
    }

    [TestMethod]
    public async Task Sha256Async_MatchesSync()
    {
        string temp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(temp, "hello lanpe");
            Assert.AreEqual(HashUtil.Sha256File(temp), await HashUtil.Sha256FileAsync(temp));
        }
        finally { File.Delete(temp); }
    }

    [TestMethod]
    public async Task Verify_EmptyExpected_ReturnsTrue()
    {
        string temp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(temp, "data");
            Assert.IsTrue(await HashUtil.VerifyAsync(temp, ""));
        }
        finally { File.Delete(temp); }
    }

    [TestMethod]
    public async Task Verify_Mismatch_ReturnsFalse()
    {
        string temp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(temp, "data");
            Assert.IsFalse(await HashUtil.VerifyAsync(temp, "deadbeef"));
        }
        finally { File.Delete(temp); }
    }

    [TestMethod]
    public void Equals_IsCaseInsensitive() => Assert.IsTrue(HashUtil.Equals("ABCDEF", "abcdef"));
}
