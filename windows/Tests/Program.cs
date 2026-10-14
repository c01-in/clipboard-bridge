using ClipboardBridge;
using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;

int checks = 0;
void Assert(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
byte[] unicode = Encoding.UTF8.GetBytes("Windows → 三星：中文、emoji 📋\n第二行");
string box = Bridge.Seal(unicode, key);
Assert(Bridge.Open(box, key).SequenceEqual(unicode), "Unicode text encryption round trip");
Assert(Bridge.Seal(unicode, key) != box, "Unique nonce for repeated plaintext");
var damaged = Convert.FromBase64String(box); damaged[15] ^= 1;
try { Bridge.Open(Convert.ToBase64String(damaged), key); throw new Exception("tamper accepted"); } catch (CryptographicException) { Assert(true, "Reject tampered ciphertext"); }
try { Bridge.Open(box, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))); throw new Exception("wrong key accepted"); } catch (CryptographicException) { Assert(true, "Reject wrong pairing key"); }
var image = RandomNumberGenerator.GetBytes(2 * 1024 * 1024);
Assert(Bridge.Open(Bridge.Seal(image, key), key).SequenceEqual(image), "2 MiB binary payload integrity");
using var stream = new MemoryStream(); await Bridge.WriteFrame(stream, image); stream.Position = 0;
Assert((await Bridge.ReadFrame(stream)).SequenceEqual(image), "Length-framed IPC binary round trip");
var prefix = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(prefix, Bridge.MaxBytes * 2 + 1);
try { await Bridge.ReadFrame(new MemoryStream(prefix)); throw new Exception("oversized frame accepted"); } catch (InvalidDataException) { Assert(true, "Reject oversized IPC frame before allocation"); }
var protectedBytes = ProtectedData.Protect(unicode, null, DataProtectionScope.LocalMachine);
Assert(ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.LocalMachine).SequenceEqual(unicode), "Persistent Windows DPAPI protection");
Console.WriteLine($"{checks} checks passed");
