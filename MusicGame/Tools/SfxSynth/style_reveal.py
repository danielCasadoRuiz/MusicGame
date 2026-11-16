"""Layered original synth reveal + existing CC0 Kenney textures. Impact at 126ms."""
from pathlib import Path
import numpy as np
from scipy import signal
import soundfile as sf
ROOT=Path(__file__).resolve().parents[2]
SR=44100; OS=4; fs=SR*OS; dur=1.35
rng=np.random.default_rng(519)
t=np.arange(round(dur*fs))/fs
# Rounded pickup accelerates into a bright held note at the visual overshoot.
k=np.clip(t/.126,0,1);q=np.maximum(0,t-.126)
f=164.8138*2**((7*(k*k*(3-2*k)))/12)
f*=1+.003*np.sin(2*np.pi*5*t)*np.clip(q/.12,0,1)
phase=2*np.pi*np.cumsum(f)/fs
attack=1-np.exp(-t*160)
env=attack*np.exp(-np.maximum(0,t-.24)*7)
y=np.zeros((len(t),2))
for ch in range(2):
 voice=np.zeros(len(t))
 for cents in (-7,-2,3,8):
  ph=phase*2**((cents+(ch*2-1)*1.3)/1200)+rng.uniform(-.2,.2)
  saw=signal.sawtooth(ph)
  # Two resonant formants morph from a closed 'wo' to an open 'hoo'.
  # Filter endpoints are crossfaded smoothly during the pitch rise.
  closed=np.zeros(len(t));opened=np.zeros(len(t))
  for hz,gain,bw in ((380,1,240),(900,.48,320),(2100,.12,500)):
   b,a=signal.iirpeak(hz,bw and hz/bw,fs=fs);closed+=gain*signal.lfilter(b,a,saw)
  for hz,gain,bw in ((720,1,430),(1250,.65,550),(2600,.20,1100)):
   b,a=signal.iirpeak(hz,hz/bw,fs=fs);opened+=gain*signal.lfilter(b,a,saw)
  voice+=(closed*(1-k)+opened*k)/4
 body=np.sin(phase*.5)*.12+np.sin(phase)*.08
 air=signal.sosfilt(signal.butter(2,[2200,8500],btype='bandpass',fs=fs,output='sos'),rng.standard_normal(len(t)))
 air*=np.exp(-((t-.105)/.045)**2)*.04
 y[:,ch]=np.tanh((voice*1.9+body)*1.3)*env+air
# Oversampling keeps the brighter harmonics free of coarse digital aliasing.
y=signal.resample_poly(y,1,OS,axis=0)
N=len(y)
def layer(name,gain,rate=1.,start=0.,highpass=0):
 p=ROOT/'Assets/_Project/Audio/SFX/Kenney/Digital'/name
 a,sr=sf.read(p,always_2d=True)
 a=signal.resample(a,round(len(a)*SR/sr/rate),axis=0)
 if a.shape[1]==1:a=np.repeat(a,2,axis=1)
 if highpass:a=signal.sosfilt(signal.butter(2,highpass,btype='highpass',fs=SR,output='sos'),a,axis=0)
 a/=max(1e-6,np.max(abs(a)))
 a*=np.minimum(1,np.arange(len(a))/max(1,SR*.004))[:,None]
 a*=np.minimum(1,np.arange(len(a))[::-1]/max(1,SR*.035))[:,None]
 off=round(start*SR);count=min(len(a),N-off)
 y[off:off+count]+=a[:count]*gain
layer('phaserUp6.ogg',.11,1.6,0,700)
layer('powerUp7.ogg',.15,1.15,.10,250)
# Dense, short room diffusion; no audible rhythmic echo repeats.
dry=y.copy()
for ch in range(2):
 ir=np.zeros(round(.28*SR)); ir[0]=0
 for z in range(90):
  delay=rng.integers(round(.013*SR),len(ir))
  ir[delay]+=rng.uniform(-1,1)*np.exp(-delay/(SR*.065))*.055
 y[:,ch]+=signal.fftconvolve(dry[:,ch],ir)[:N]
y=np.tanh(y*1.5)
y=signal.sosfilt(signal.butter(2,35,btype='highpass',fs=SR,output='sos'),y,axis=0)
y*=np.minimum(1,np.arange(N)[::-1]/(SR*.12))[:,None]
y*=.91/max(1e-6,np.max(abs(y)))
assert np.isfinite(y).all()
path=ROOT/'Assets/_Project/Audio/SFX/StyleRevealMagic.wav'
sf.write(path,y,SR,subtype='PCM_16')
review=ROOT.parent/'VisualReviews/StyleReveal/StyleReveal-produced-woohoo.wav'
sf.write(review,y,SR,subtype='PCM_16')
print(f'Written {len(y)/SR:.2f}s stereo. Peak {20*np.log10(np.max(abs(y))):.2f}dBFS; RMS {20*np.log10(np.sqrt(np.mean(y*y))):.2f}dBFS.')
