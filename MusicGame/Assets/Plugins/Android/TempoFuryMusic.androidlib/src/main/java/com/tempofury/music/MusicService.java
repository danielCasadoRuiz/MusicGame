package com.tempofury.music;

import android.app.*;
import android.content.*;
import android.media.*;
import android.media.session.*;
import android.os.*;
import org.json.*;

/** Native queue runs without Unity frames. Private service; all inputs are local app-owned files. */
public class MusicService extends Service {
    private static volatile MusicService instance;
    private static volatile int currentIndex = -1;
    private static volatile boolean playing;
    private static volatile String error = "";
    private static volatile float volume = 1f;
    private static volatile float positionSeconds;
    private static final String CHANNEL = "tf_music";
    private static final int NOTIFICATION = 741;
    private MediaPlayer player;
    private TpsDataSource protectedAudio;
    private MediaSession session;
    private AudioManager audio;
    private AudioFocusRequest focus;
    private JSONArray tracks;
    private boolean prepared, resumeOnFocus;
    private int generation;
    private final Handler main = new Handler(Looper.getMainLooper());
    private final Runnable positionPoll = new Runnable() {
        @Override public void run() {
            try { positionSeconds = prepared && player != null ? player.getCurrentPosition() / 1000f : 0f; }
            catch (IllegalStateException ignored) { positionSeconds = 0f; }
            main.postDelayed(this, 100);
        }
    };
    private final BroadcastReceiver noisy = new BroadcastReceiver() {
        @Override public void onReceive(Context c, Intent i) { pause(); }
    };
    private final AudioManager.OnAudioFocusChangeListener focusChanged = value -> {
        if (value == AudioManager.AUDIOFOCUS_GAIN) {
            if (player != null) player.setVolume(volume, volume);
            if (resumeOnFocus) { resumeOnFocus = false; resume(); }
        } else if (value == AudioManager.AUDIOFOCUS_LOSS_TRANSIENT_CAN_DUCK) {
            if (player != null) player.setVolume(volume * .2f, volume * .2f);
        } else {
            boolean wasPlaying = playing; pause();
            resumeOnFocus = wasPlaying && value == AudioManager.AUDIOFOCUS_LOSS_TRANSIENT;
        }
    };
    public static void start(Activity activity, String json, int index) {
        error = ""; currentIndex = -1; playing = false;
        Intent intent = new Intent(activity, MusicService.class).setAction("queue").putExtra("tracks", json).putExtra("index", index);
        if (Build.VERSION.SDK_INT >= 26) activity.startForegroundService(intent); else activity.startService(intent);
    }
    public static void command(String command) {
        MusicService service = instance;
        if (service != null) service.main.post(() -> service.handle(command));
    }
    public static int index() { return currentIndex; }
    public static boolean isPlaying() { return playing; }
    public static float positionSeconds() { return positionSeconds; }
    public static String error() { return error; }
    public static void volume(float value) {
        volume = Math.max(0f, Math.min(1f, value));
        MusicService service = instance;
        if (service != null) service.main.post(() -> { if (service.player != null) service.player.setVolume(volume, volume); });
    }
    @Override public void onCreate() {
        super.onCreate(); instance = this; main.post(positionPoll);
        audio = (AudioManager)getSystemService(AUDIO_SERVICE);
        if (Build.VERSION.SDK_INT >= 26) {
            ((NotificationManager)getSystemService(NOTIFICATION_SERVICE)).createNotificationChannel(new NotificationChannel(CHANNEL, "Jukebox", NotificationManager.IMPORTANCE_LOW));
            focus = new AudioFocusRequest.Builder(AudioManager.AUDIOFOCUS_GAIN)
                .setAudioAttributes(new AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_MEDIA).setContentType(AudioAttributes.CONTENT_TYPE_MUSIC).build())
                .setOnAudioFocusChangeListener(focusChanged).build();
        }
        session = new MediaSession(this, "Tempo Fury Jukebox");
        session.setCallback(new MediaSession.Callback() {
            @Override public void onPlay() { resume(); }
            @Override public void onPause() { pause(); }
            @Override public void onSkipToNext() { skip(1); }
            @Override public void onSkipToPrevious() { skip(-1); }
            @Override public void onStop() { stopSelf(); }
            @Override public void onSeekTo(long position) { if (prepared && player != null) { player.seekTo((int)position); publish(); } }
        });
        session.setFlags(MediaSession.FLAG_HANDLES_MEDIA_BUTTONS | MediaSession.FLAG_HANDLES_TRANSPORT_CONTROLS);
        session.setSessionActivity(openApp()); session.setActive(true);
        if (Build.VERSION.SDK_INT >= 33) registerReceiver(noisy, new IntentFilter(AudioManager.ACTION_AUDIO_BECOMING_NOISY), Context.RECEIVER_NOT_EXPORTED);
        else registerReceiver(noisy, new IntentFilter(AudioManager.ACTION_AUDIO_BECOMING_NOISY));
    }
    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        if (intent == null) { stopSelf(); return START_NOT_STICKY; }
        if ("queue".equals(intent.getAction())) {
            startForeground(NOTIFICATION, notification("Tempo Fury", "Jukebox"));
            try { tracks = new JSONArray(intent.getStringExtra("tracks")); load(intent.getIntExtra("index", 0)); }
            catch (Exception e) { fail(e); }
        } else handle(intent.getAction());
        return START_NOT_STICKY;
    }
    private void handle(String command) {
        if ("pause".equals(command)) pause();
        else if ("play".equals(command)) resume();
        else if ("toggle".equals(command)) { if (playing) pause(); else resume(); }
        else if ("next".equals(command)) skip(1);
        else if ("previous".equals(command)) skip(-1);
        else if ("stop".equals(command)) stopSelf();
    }
    private void load(int index) {
        releasePlayer();
        if (tracks == null || index < 0 || index >= tracks.length()) { stopSelf(); return; }
        int token = ++generation;
        try {
            JSONObject track = tracks.getJSONObject(index);
            currentIndex = index; prepared = false; playing = false;
            player = new MediaPlayer();
            player.setVolume(volume, volume);
            player.setAudioAttributes(new AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_MEDIA).setContentType(AudioAttributes.CONTENT_TYPE_MUSIC).build());
            player.setWakeMode(this, PowerManager.PARTIAL_WAKE_LOCK);
            String path = track.getString("path");
            if (path.endsWith(".tps")) {
                protectedAudio = new TpsDataSource(path, track.getString("key"));
                player.setDataSource(protectedAudio);
            } else player.setDataSource(path); // Imported user audio only; game originals are TPS.

            player.setOnPreparedListener(p -> { if (generation == token) { prepared = true; resume(); } });
            player.setOnCompletionListener(p -> load(currentIndex + 1)); // finish at the end; never an implicit loop
            player.setOnErrorListener((p, what, extra) -> { fail(new Exception("MediaPlayer " + what + "/" + extra)); return true; });
            player.prepareAsync(); publish();
        } catch (Exception e) { fail(e); }
    }
    private void skip(int delta) { if (tracks != null && tracks.length() > 0) load((currentIndex + delta + tracks.length()) % tracks.length()); }
    private void resume() {
        if (player == null || !prepared) return;
        int granted = Build.VERSION.SDK_INT >= 26 ? audio.requestAudioFocus(focus) : audio.requestAudioFocus(focusChanged, AudioManager.STREAM_MUSIC, AudioManager.AUDIOFOCUS_GAIN);
        if (granted != AudioManager.AUDIOFOCUS_REQUEST_GRANTED) { playing = false; publish(); return; }
        player.start(); playing = true; publish();
    }
    private void pause() { resumeOnFocus = false; if (player != null && prepared) player.pause(); playing = false; publish(); }
    private void publish() {
        if (session == null) return;
        long position = prepared && player != null ? player.getCurrentPosition() : 0;
        session.setPlaybackState(new PlaybackState.Builder().setActions(PlaybackState.ACTION_PLAY | PlaybackState.ACTION_PAUSE | PlaybackState.ACTION_PLAY_PAUSE | PlaybackState.ACTION_SKIP_TO_NEXT | PlaybackState.ACTION_SKIP_TO_PREVIOUS | PlaybackState.ACTION_STOP | PlaybackState.ACTION_SEEK_TO)
            .setState(playing ? PlaybackState.STATE_PLAYING : PlaybackState.STATE_PAUSED, position, playing ? 1 : 0).build());
        try {
            JSONObject track = tracks.getJSONObject(currentIndex);
            String title = track.optString("title"), artist = track.optString("artist");
            session.setMetadata(new MediaMetadata.Builder().putString(MediaMetadata.METADATA_KEY_TITLE, title).putString(MediaMetadata.METADATA_KEY_ARTIST, artist)
                .putLong(MediaMetadata.METADATA_KEY_DURATION, prepared && player != null ? player.getDuration() : 0).build());
            ((NotificationManager)getSystemService(NOTIFICATION_SERVICE)).notify(NOTIFICATION, notification(title, artist));
        } catch (Exception ignored) { }
    }
    private PendingIntent action(String name, int code) {
        return PendingIntent.getService(this, code, new Intent(this, MusicService.class).setAction(name), PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
    }
    private PendingIntent openApp() {
        Intent launch = getPackageManager().getLaunchIntentForPackage(getPackageName());
        return launch == null ? null : PendingIntent.getActivity(this, 0, launch, PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
    }
    private Notification notification(String title, String artist) {
        Notification.Builder builder = Build.VERSION.SDK_INT >= 26 ? new Notification.Builder(this, CHANNEL) : new Notification.Builder(this);
        return builder.setSmallIcon(android.R.drawable.ic_media_play).setContentTitle(title).setContentText(artist).setContentIntent(openApp()).setVisibility(Notification.VISIBILITY_PUBLIC)
            .setOngoing(playing).setDeleteIntent(action("stop", 5))
            .addAction(new Notification.Action.Builder(android.R.drawable.ic_media_previous, "Previous", action("previous", 1)).build())
            .addAction(new Notification.Action.Builder(playing ? android.R.drawable.ic_media_pause : android.R.drawable.ic_media_play, playing ? "Pause" : "Play", action("toggle", 2)).build())
            .addAction(new Notification.Action.Builder(android.R.drawable.ic_media_next, "Next", action("next", 3)).build())
            .setStyle(new Notification.MediaStyle().setMediaSession(session.getSessionToken()).setShowActionsInCompactView(0, 1, 2)).build();
    }
    private void fail(Exception e) { error = e.toString(); stopSelf(); }
    private void releasePlayer() { generation++; prepared = false; if (player != null) { player.release(); player = null; } if (protectedAudio != null) { protectedAudio.close(); protectedAudio = null; } playing = false; }
    @Override public void onDestroy() {
        main.removeCallbacks(positionPoll); releasePlayer(); positionSeconds = 0f; currentIndex = -1; instance = null;
        if (session != null) { session.setActive(false); session.release(); session = null; }
        if (Build.VERSION.SDK_INT >= 26 && focus != null) audio.abandonAudioFocusRequest(focus); else audio.abandonAudioFocus(focusChanged);
        try { unregisterReceiver(noisy); } catch (Exception ignored) { }
        stopForeground(true); super.onDestroy();
    }
    @Override public IBinder onBind(Intent intent) { return null; }
}
