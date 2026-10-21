package com.colin.clipbridge;

import android.app.Activity;
import android.content.Intent;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import java.math.BigInteger;
import java.security.KeyPairGenerator;
import java.security.interfaces.ECPublicKey;
import java.security.spec.ECGenParameterSpec;
import java.util.Base64;
import org.json.JSONObject;
import org.junit.Test;
import org.junit.runner.RunWith;
import static org.junit.Assert.*;
import static org.junit.Assume.assumeTrue;

/** Explicit physical-device smoke test; offline CI must not depend on Google. */
@RunWith(AndroidJUnit4.class)
public final class FreshRegistrationTests {
    @Test public void firstInstallCanRegisterWithRealGoogleServices()throws Exception {
        assumeTrue("Manual Google-network test only", "true".equals(InstrumentationRegistry.getArguments().getString("realGoogle")));
        var instrumentation=InstrumentationRegistry.getInstrumentation();var context=instrumentation.getTargetContext();
        assertTrue("Never touch the release app",context.getPackageName().endsWith(".debug"));
        assertFalse("Requires a fresh debug installation",Config.prefs(context).contains("config"));
        assertFalse("Requires no cached subscription",Config.prefs(context).contains("subscription"));
        Config.prefs(context).edit().putBoolean("notificationIntroShown",true).commit();
        KeyPairGenerator generator=KeyPairGenerator.getInstance("EC");generator.initialize(new ECGenParameterSpec("secp256r1"));
        ECPublicKey key=(ECPublicKey)generator.generateKeyPair().getPublic();byte[] point=new byte[65];point[0]=4;
        coordinate(key.getW().getAffineX(),point,1);coordinate(key.getW().getAffineY(),point,33);
        String vapid=Base64.getUrlEncoder().withoutPadding().encodeToString(point);
        Activity activity=instrumentation.startActivitySync(new Intent(context,MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        try {
            JSONObject subscription=PushRegistration.ensure(context,new JSONObject().put("vapid",vapid));
            assertTrue(subscription.getString("endpoint").startsWith("https://fcm.googleapis.com/"));
            assertEquals(65,Base64.getUrlDecoder().decode(subscription.getJSONObject("keys").getString("p256dh")).length);
            assertEquals(16,Base64.getUrlDecoder().decode(subscription.getJSONObject("keys").getString("auth")).length);
            assertFalse("Existing PC pairing was not used",Config.prefs(context).contains("config"));
        } finally {instrumentation.runOnMainSync(activity::finish);}
    }
    private static void coordinate(BigInteger value,byte[] point,int offset){
        byte[] source=value.toByteArray();int length=Math.min(32,source.length);
        System.arraycopy(source,source.length-length,point,offset+32-length,length);
    }
}
