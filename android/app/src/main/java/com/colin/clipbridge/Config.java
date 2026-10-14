package com.colin.clipbridge;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import com.google.firebase.FirebaseApp;
import com.google.firebase.FirebaseOptions;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;
import org.json.JSONObject;

public final class Config {
    static SharedPreferences prefs(Context c){return c.getSharedPreferences("bridge",Context.MODE_PRIVATE);}
    private static SecretKey storageKey() throws Exception {
        KeyStore s=KeyStore.getInstance("AndroidKeyStore");s.load(null);
        if(!s.containsAlias("clipbridge-config")) {KeyGenerator g=KeyGenerator.getInstance("AES","AndroidKeyStore");g.init(new KeyGenParameterSpec.Builder("clipbridge-config",KeyProperties.PURPOSE_ENCRYPT|KeyProperties.PURPOSE_DECRYPT).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build());g.generateKey();}
        return (SecretKey)s.getKey("clipbridge-config",null);
    }
    static void save(Context c,JSONObject value) throws Exception {
        Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.ENCRYPT_MODE,storageKey());
        String body=Base64.encodeToString(cipher.doFinal(value.toString().getBytes(StandardCharsets.UTF_8)),Base64.NO_WRAP),iv=Base64.encodeToString(cipher.getIV(),Base64.NO_WRAP);
        if(!prefs(c).edit().putString("config",body).putString("iv",iv).remove("lastSequence").remove("latestScheduledSequence").commit())throw new Exception("无法保存配对配置");
    }
    static JSONObject read(Context c) throws Exception {
        SharedPreferences p=prefs(c);if(!p.contains("config"))throw new Exception("请先与 Windows 配对");
        Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.DECRYPT_MODE,storageKey(),new GCMParameterSpec(128,Base64.decode(p.getString("iv",""),Base64.NO_WRAP)));
        return new JSONObject(new String(cipher.doFinal(Base64.decode(p.getString("config",""),Base64.NO_WRAP)),StandardCharsets.UTF_8));
    }
    static synchronized void initialize(Context c) throws Exception {
        if(!FirebaseApp.getApps(c).isEmpty())return;
        JSONObject fb=read(c).getJSONObject("firebase"),project=fb.getJSONObject("project_info"),client=null;
        for(int i=0;i<fb.getJSONArray("client").length();i++){JSONObject x=fb.getJSONArray("client").getJSONObject(i);if(x.getJSONObject("client_info").getJSONObject("android_client_info").getString("package_name").equals(c.getPackageName())){client=x;break;}}
        if(client==null)throw new Exception("Firebase Android 包名不匹配");
        FirebaseOptions options=new FirebaseOptions.Builder().setApplicationId(client.getJSONObject("client_info").getString("mobilesdk_app_id")).setApiKey(client.getJSONArray("api_key").getJSONObject(0).getString("current_key")).setGcmSenderId(project.getString("project_number")).setProjectId(project.getString("project_id")).build();
        FirebaseApp.initializeApp(c,options);
    }
    static void status(Context c,String message){prefs(c).edit().putString("status",message).apply();}
}
