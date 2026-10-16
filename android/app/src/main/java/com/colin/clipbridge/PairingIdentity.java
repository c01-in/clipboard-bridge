package com.colin.clipbridge;

import java.nio.charset.StandardCharsets;
import java.security.*;
import java.security.spec.*;
import java.util.Base64;
import javax.crypto.*;
import javax.crypto.spec.SecretKeySpec;

/** P-256 + transcript-bound HKDF-SHA256, interoperable with the Windows pairing service. */
final class PairingIdentity {
    static final String PROTOCOL="ClipboardBridge.Pairing.v1";
    final KeyPair keys;
    final String nonce;
    PairingIdentity()throws Exception {
        KeyPairGenerator g=KeyPairGenerator.getInstance("EC");g.initialize(new ECGenParameterSpec("secp256r1"));keys=g.generateKeyPair();
        byte[] n=new byte[32];new SecureRandom().nextBytes(n);nonce=Base64.getEncoder().encodeToString(n);
    }
    String publicKey(){return Base64.getEncoder().encodeToString(keys.getPublic().getEncoded());}
    String agree(String publicKey,String transcript)throws Exception {
        java.security.interfaces.ECPublicKey peer=(java.security.interfaces.ECPublicKey)KeyFactory.getInstance("EC").generatePublic(new X509EncodedKeySpec(Base64.getDecoder().decode(publicKey)));
        java.security.interfaces.ECPublicKey own=(java.security.interfaces.ECPublicKey)keys.getPublic();
        if(!peer.getParams().getCurve().equals(own.getParams().getCurve())||!peer.getParams().getGenerator().equals(own.getParams().getGenerator())||!peer.getParams().getOrder().equals(own.getParams().getOrder()))throw new GeneralSecurityException("Invalid pairing curve");
        KeyAgreement agreement=KeyAgreement.getInstance("ECDH");agreement.init(keys.getPrivate());agreement.doPhase(peer,true);byte[] secret=agreement.generateSecret();
        byte[] salt=MessageDigest.getInstance("SHA-256").digest(transcript.getBytes(StandardCharsets.UTF_8));
        byte[] prk=hmac(salt,secret);byte[] info=(PROTOCOL+"\u0001").getBytes(StandardCharsets.UTF_8);byte[] key=hmac(prk,info);
        java.util.Arrays.fill(secret,(byte)0);java.util.Arrays.fill(prk,(byte)0);return Base64.getEncoder().encodeToString(key);
    }
    static byte[] hmac(byte[] key,byte[] body)throws Exception {Mac mac=Mac.getInstance("HmacSHA256");mac.init(new SecretKeySpec(key,"HmacSHA256"));return mac.doFinal(body);}
    static String proof(String key,String message)throws Exception {return Base64.getEncoder().encodeToString(hmac(Base64.getDecoder().decode(key),message.getBytes(StandardCharsets.UTF_8)));}
    static boolean verify(String key,String message,String proof)throws Exception {try{return MessageDigest.isEqual(Base64.getDecoder().decode(proof(key,message)),Base64.getDecoder().decode(proof));}catch(IllegalArgumentException e){return false;}}
    static String code(String key)throws Exception {byte[] b=Base64.getDecoder().decode(proof(key,"confirmation-code"));long n=java.nio.ByteBuffer.wrap(b).getInt()&0xffffffffL;return String.format(java.util.Locale.ROOT,"%06d",n%1000000);}
    static String name(String value){return Base64.getEncoder().encodeToString(value.getBytes(StandardCharsets.UTF_8));}
}
