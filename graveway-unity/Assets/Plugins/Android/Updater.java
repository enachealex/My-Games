// Graveway installing a newer build of itself (Scripts/Core/Updates.cs drives this).
//
// The APK the game downloaded is written into a PackageInstaller session ahead of time
// ("prepare"), so putting it in later ("commit") is quick: the game commits when it goes
// into the background, and Android swaps the app while nobody's looking. On Android 12 and
// up an app may update itself without asking (USER_ACTION_NOT_REQUIRED, with the
// UPDATE_PACKAGES_WITHOUT_USER_ACTION permission in the manifest); before that, or when a
// phone insists, Android shows its own "Update?" screen instead, which the game shows when
// it's in front. Either way the player has to have let Graveway install apps once
// (Settings -> Install unknown apps), and the new build must be signed with the same key,
// or Android refuses it.

package com.thejumpvault.graveway;

import android.app.Activity;
import android.app.PendingIntent;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.pm.PackageInstaller;
import android.net.Uri;
import android.os.Build;
import android.provider.Settings;

import java.io.File;
import java.io.FileInputStream;
import java.io.InputStream;
import java.io.OutputStream;

public final class Updater {
    private Updater() {}

    private static final String ACTION = ".GRAVEWAY_INSTALL_STATUS";

    // "" nothing yet; "pending" committed; "waiting" Android wants the player's OK;
    // "done"; "failed: <why>".
    private static volatile String status = "";
    private static volatile Intent confirm;           // Android's "Update?" screen, when it wants one
    private static volatile boolean foreground = true;
    private static BroadcastReceiver receiver;

    public static String status() { return status; }

    public static void clearStatus() { status = ""; confirm = null; }

    // Whether the player has let Graveway install apps (Install unknown apps).
    public static boolean canInstall(Context c) {
        return c.getPackageManager().canRequestPackageInstalls();
    }

    // Whether Android lets it update itself without asking (12 and up).
    public static boolean silent() {
        return Build.VERSION.SDK_INT >= 31;
    }

    // Android's page for letting Graveway install apps.
    public static void openInstallSettings(Activity a) {
        Intent i = new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:" + a.getPackageName()));
        a.startActivity(i);
    }

    // The game tells it when it's in front: Android's "Update?" screen is only shown then.
    public static void setForeground(boolean on) { foreground = on; }

    // Writes the APK into a new install session (dropping any older one of ours), without
    // committing it. Returns the session's id, or -1 (status says why).
    public static int prepare(Context c, String apkPath) {
        PackageInstaller installer = c.getPackageManager().getPackageInstaller();
        for (PackageInstaller.SessionInfo s : installer.getMySessions()) {
            try { installer.abandonSession(s.getSessionId()); } catch (Exception ignored) {}
        }
        File apk = new File(apkPath);
        PackageInstaller.SessionParams params = new PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL);
        params.setAppPackageName(c.getPackageName());
        params.setSize(apk.length());
        if (Build.VERSION.SDK_INT >= 31) params.setRequireUserAction(PackageInstaller.SessionParams.USER_ACTION_NOT_REQUIRED);
        int id = -1;
        try {
            id = installer.createSession(params);
            try (PackageInstaller.Session session = installer.openSession(id);
                 InputStream in = new FileInputStream(apk);
                 OutputStream out = session.openWrite("graveway.apk", 0, apk.length())) {
                byte[] buf = new byte[1 << 16];
                int n;
                while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
                session.fsync(out);
            }
            status = "";
            return id;
        } catch (Exception e) {
            status = "failed: " + e.getMessage();
            if (id >= 0) {
                try { installer.abandonSession(id); } catch (Exception ignored) {}
            }
            return -1;
        }
    }

    // Whether a prepared session is still there (they outlive the app being closed).
    public static boolean has(Context c, int id) {
        return id >= 0 && c.getPackageManager().getPackageInstaller().getSessionInfo(id) != null;
    }

    // Puts the update in. When it works, Android closes the game to swap it; the next time
    // it's opened, it's the new build.
    public static boolean commit(Context c, int id) {
        Context app = c.getApplicationContext();
        String action = app.getPackageName() + ACTION;
        try {
            if (receiver == null) {
                receiver = new BroadcastReceiver() {
                    @Override
                    public void onReceive(Context ctx, Intent intent) {
                        int s = intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE);
                        if (s == PackageInstaller.STATUS_PENDING_USER_ACTION) {
                            confirm = intent.getParcelableExtra(Intent.EXTRA_INTENT);
                            status = "waiting";
                            if (foreground) showConfirm(ctx);
                        } else if (s == PackageInstaller.STATUS_SUCCESS) {
                            status = "done";
                        } else {
                            String why = intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE);
                            status = "failed: " + (why != null ? why : "status " + s);
                        }
                    }
                };
                IntentFilter filter = new IntentFilter(action);
                // The status comes back through our own PendingIntent, so as us: not exported.
                if (Build.VERSION.SDK_INT >= 33) app.registerReceiver(receiver, filter, Context.RECEIVER_NOT_EXPORTED);
                else app.registerReceiver(receiver, filter);
            }
            Intent back = new Intent(action).setPackage(app.getPackageName());
            int flags = PendingIntent.FLAG_UPDATE_CURRENT | (Build.VERSION.SDK_INT >= 31 ? PendingIntent.FLAG_MUTABLE : 0);
            PendingIntent pending = PendingIntent.getBroadcast(app, id, back, flags);
            PackageInstaller.Session session = app.getPackageManager().getPackageInstaller().openSession(id);
            session.commit(pending.getIntentSender());
            session.close();
            status = "pending";
            return true;
        } catch (Exception e) {
            status = "failed: " + e.getMessage();
            return false;
        }
    }

    // Android's "Update?" screen, if it asked for one.
    public static boolean showConfirm(Context c) {
        Intent i = confirm;
        if (i == null) return false;
        try {
            i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            c.startActivity(i);
            return true;
        } catch (Exception e) {
            status = "failed: " + e.getMessage();
            return false;
        }
    }
}
