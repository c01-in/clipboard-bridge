package com.colin.clipbridge;

import java.nio.charset.StandardCharsets;
import java.security.SecureRandom;
import java.util.Arrays;
import javax.crypto.Cipher;
import javax.crypto.spec.GCMParameterSpec;
import javax.crypto.spec.SecretKeySpec;
import android.util.Base64;

public final class Crypto {
    private static final byte[] AAD = "ClipboardBridge/v1".getBytes(StandardCharsets.UTF_8);
    public static String seal(byte[] data, String key) throws Exception {
        byte[] iv = new byte[12]; new SecureRandom().nextBytes(iv);
        Cipher c=Cipher.getInstance("AES/GCM/NoPadding"); c.init(Cipher.ENCRYPT_MODE,new SecretKeySpec(Base64.decode(key,Base64.NO_WRAP),"AES"),new GCMParameterSpec(128,iv)); c.updateAAD(AAD);
        byte[] encrypted=c.doFinal(data), result=new byte[iv.length+encrypted.length]; System.arraycopy(iv,0,result,0,12);System.arraycopy(encrypted,0,result,12,encrypted.length);
        return Base64.encodeToString(result,Base64.NO_WRAP);
    }
    public static byte[] open(String box,String key) throws Exception {
        byte[] data=Base64.decode(box,Base64.NO_WRAP); if(data.length<28 || data.length>16*1024*1024+28)throw new Exception(LocalText.get(R.string.error_response_size));
        Cipher c=Cipher.getInstance("AES/GCM/NoPadding"); c.init(Cipher.DECRYPT_MODE,new SecretKeySpec(Base64.decode(key,Base64.NO_WRAP),"AES"),new GCMParameterSpec(128,Arrays.copyOfRange(data,0,12))); c.updateAAD(AAD);return c.doFinal(data,12,data.length-12);
    }
}
