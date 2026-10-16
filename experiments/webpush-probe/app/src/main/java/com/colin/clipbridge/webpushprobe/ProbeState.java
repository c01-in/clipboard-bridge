package com.colin.clipbridge.webpushprobe;

import android.content.Context;
import android.os.PowerManager;
import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.StandardOpenOption;
import org.json.JSONObject;

final class ProbeState {
    static volatile boolean visible;
    static synchronized void write(Context c, String name, JSONObject value) throws Exception {
        File target = new File(c.getFilesDir(), name);
        Files.write(target.toPath(), value.toString().getBytes(StandardCharsets.UTF_8));
    }
    static synchronized void event(Context c, String kind, JSONObject fields) {
        try {
            PowerManager power = c.getSystemService(PowerManager.class);
            fields.put("kind", kind).put("receivedAt", System.currentTimeMillis())
                .put("pid", android.os.Process.myPid()).put("foreground", visible)
                .put("interactive", power.isInteractive()).put("deviceIdle", power.isDeviceIdleMode());
            Files.write(new File(c.getFilesDir(), "events.jsonl").toPath(),
                (fields.toString()+"\n").getBytes(StandardCharsets.UTF_8),
                StandardOpenOption.CREATE, StandardOpenOption.APPEND);
            c.getSharedPreferences("probe", 0).edit().putString("status", fields.toString(2)).apply();
            // Logs intentionally contain no subscription endpoint, key, or auth secret.
            android.util.Log.i("WebPushProbe", "event="+kind+" pid="+android.os.Process.myPid());
        } catch (Exception error) { android.util.Log.e("WebPushProbe", "Cannot persist evidence: "+error.getClass().getSimpleName()); }
    }
}
