import json, os, sys
out = sys.argv[1]
L = ["quality","balanced","maxfps","competitive"]
def note(tr,en,f="medium",v="medium"): return {"text":{"tr":tr,"en":en},"fpsImpact":f,"visualImpact":v}
def prof(id,name,ac,comp,group,detect,launch,configs=None,never=None,levels=None,gpu=None,recommend=None,notes=None,tips=None,stretch=None):
    lv={}
    for l in L:
        spec={"set":(levels or {}).get(l,{})}
        g=dict((gpu or {}).get("all",{})); g.update((gpu or {}).get(l,{}))
        if comp and l=="competitive": g.update({"radeonBoost":False,"upscaler":"off","lowLatency":True,"vSync":False})
        if comp and g.get("upscaler")=="quality": g["upscaler"]="off"
        if g: spec["gpu"]=g
        lv[l]=spec
    p={"id":id,"name":name,"schemaVersion":1,"antiCheat":ac,"competitive":comp,"group":group,"detect":detect,"launch":launch,
       "configs":configs or [],"neverTouch":never or [],"levels":lv,
       "recommend":recommend or ([{"level":"competitive"}] if comp else [{"ifScoreBelow":40,"level":"maxfps"},{"ifScoreBelow":65,"level":"balanced"},{"level":"quality"}]),
       "notes":notes or {},"tips":tips,"verifiedOn":"2026-11"}
    if stretch: p["stretchKeys"]=stretch
    json.dump(p,open(os.path.join(out,id+".json"),"w",encoding="utf-8"),ensure_ascii=False,indent=2)

SINGLE={"all":{"lowLatency":True,"vSync":False}}
NONCOMP_GPU={"all":{"lowLatency":True,"vSync":False},"quality":{"radeonBoost":False},"balanced":{"radeonBoost":True},"maxfps":{"radeonBoost":True,"upscaler":"quality"},"competitive":{"radeonBoost":True,"upscaler":"quality"}}

# ---- Unreal Engine games: ScalabilityGroups (0 low .. 3 epic) ----
SG=["ViewDistanceQuality","AntiAliasingQuality","ShadowQuality","GlobalIlluminationQuality","ReflectionQuality","PostProcessQuality","TextureQuality","EffectsQuality","FoliageQuality","ShadingQuality"]
def ue_levels(cid, vsyncKey=None, comp=False):
    q={"quality":3,"balanced":2,"maxfps":0,"competitive":0}
    lv={}
    for l,v in q.items():
        s={f"{cid}:ScalabilityGroups/sg.{k}":str(v) for k in SG}
        if l=="quality":
            for k in ["ShadowQuality","PostProcessQuality","EffectsQuality","AntiAliasingQuality"]: s[f"{cid}:ScalabilityGroups/sg.{k}"]="2"
        if l=="competitive":
            s[f"{cid}:ScalabilityGroups/sg.ViewDistanceQuality"]="3"  # see far enemies
            s[f"{cid}:ScalabilityGroups/sg.TextureQuality"]="1"
        if vsyncKey: s[f"{cid}:{vsyncKey}"]="False"
        lv[l]=s
    return lv
def ue_notes(cid, vsyncKey=None):
    n={f"{cid}:ScalabilityGroups/sg.ShadowQuality":note("Gölgeler","Shadows","high","medium"),
       f"{cid}:ScalabilityGroups/sg.PostProcessQuality":note("Son işleme efektleri","Post-processing","medium","low"),
       f"{cid}:ScalabilityGroups/sg.EffectsQuality":note("Efektler (duman, patlama)","Effects","medium","low"),
       f"{cid}:ScalabilityGroups/sg.ViewDistanceQuality":note("Görüş mesafesi (rekabette yüksek kalır)","View distance (kept high for competitive)","low","high"),
       f"{cid}:ScalabilityGroups/sg.FoliageQuality":note("Bitki örtüsü","Foliage","medium","medium"),
       f"{cid}:ScalabilityGroups/sg.TextureQuality":note("Doku kalitesi (VRAM)","Textures (VRAM)","low","medium")}
    if vsyncKey: n[f"{cid}:{vsyncKey}"]=note("Dikey eşitleme kapalı","V-Sync off","high","low")
    return n
UE_NEVER=lambda cid:[f"{cid}:*FrameRateLimit*",f"{cid}:*Monitor*",f"{cid}:*Gamma*",f"{cid}:*Brightness*",f"{cid}:*HDR*",f"{cid}:*Mouse*",f"{cid}:*Window*"]
UE_STRETCH=lambda cid,cls:[f"{cid}:{cls}/ResolutionSizeX",f"{cid}:{cls}/ResolutionSizeY",f"{cid}:{cls}/LastUserConfirmedResolutionSizeX",f"{cid}:{cls}/LastUserConfirmedResolutionSizeY",f"{cid}:{cls}/PreferredFullscreenMode",f"{cid}:{cls}/LastConfirmedFullscreenMode"]

cls="/Script/FortniteGame.FortGameUserSettings"
prof("fortnite","Fortnite","eac",True,"A",{"epic":"Fortnite","process":"FortniteClient-Win64-Shipping.exe"},{"epic":"Fortnite"},
     [{"id":"gus","format":"ini","path":"%LOCALAPPDATA%/FortniteGame/Saved/Config/WindowsClient/GameUserSettings.ini"}],UE_NEVER("gus"),
     ue_levels("gus",cls+"/bUseVSync"),SINGLE,None,ue_notes("gus",cls+"/bUseVSync"),
     {"tr":"En yüksek FPS için oyun içinde Görüntü > İşleme Modu: Performans'ı dene. Çözünürlük, tuşlar ve hassasiyete dokunulmadı.","en":"For max FPS try Display > Rendering Mode: Performance in game. Resolution, keybinds and sensitivity were not touched."},
     UE_STRETCH("gus",cls))
cls="/Script/TslGame.TslGameUserSettings"
prof("pubg","PUBG: BATTLEGROUNDS","battleye",True,"A",{"steam":"578080","process":"TslGame.exe"},{"steam":"578080"},
     [{"id":"gus","format":"ini","path":"%LOCALAPPDATA%/TslGame/Saved/Config/WindowsNoEditor/GameUserSettings.ini"}],UE_NEVER("gus"),
     ue_levels("gus",cls+"/bUseVSync"),SINGLE,None,ue_notes("gus",cls+"/bUseVSync"),
     {"tr":"Rekabette görüş mesafesi yüksek, doku orta tutuldu (uzaktaki rakipleri görmek için). Render ölçeği ve FPS limiti oyun içinden.","en":"For competitive, view distance stays high and textures medium (to spot far enemies). Set render scale and FPS cap in game."},
     UE_STRETCH("gus",cls))
prof("palworld","Palworld","none",False,"A",{"steam":"1623730","process":"Palworld-Win64-Shipping.exe"},{"steam":"1623730"},
     [{"id":"gus","format":"ini","path":"%LOCALAPPDATA%/Pal/Saved/Config/Windows/GameUserSettings.ini"}],UE_NEVER("gus"),
     ue_levels("gus"),NONCOMP_GPU,None,ue_notes("gus"),
     {"tr":"Oyun içinde DLSS/FSR'yi Kalite moduna al ve hareket bulanıklığını kapat.","en":"In game set DLSS/FSR to Quality and turn motion blur off."})
prof("hogwarts-legacy","Hogwarts Legacy","none",False,"A",{"steam":"990080","process":"HogwartsLegacy.exe"},{"steam":"990080"},
     [{"id":"gus","format":"ini","path":"%LOCALAPPDATA%/Hogwarts Legacy/Saved/Config/WindowsNoEditor/GameUserSettings.ini"}],UE_NEVER("gus"),
     ue_levels("gus"),NONCOMP_GPU,None,ue_notes("gus"),
     {"tr":"Işın izlemeyi kapat, FSR/DLSS Kalite aç. 6 GB altı VRAM'de doku kalitesini Orta tut.","en":"Turn ray tracing off, FSR/DLSS Quality on. Under 6 GB VRAM keep textures Medium."})

# ---- CS2 (Valve KeyValues) ----
v="video:video.cfg/"
cs={"quality":{"setting.mat_vsync":"0","setting.msaa_samples":"4","setting.r_csgo_fsr_upsample":"0"},
    "balanced":{"setting.mat_vsync":"0","setting.msaa_samples":"2","setting.videocfg_particle_detail":"1","setting.videocfg_ao_detail":"0","setting.r_csgo_fsr_upsample":"0"},
    "maxfps":{"setting.mat_vsync":"0","setting.msaa_samples":"0","setting.r_csgo_cmaa_enable":"0","setting.videocfg_shadow_quality":"0","setting.videocfg_texture_detail":"0","setting.r_texturefilteringquality":"0","setting.shaderquality":"0","setting.videocfg_particle_detail":"0","setting.videocfg_ao_detail":"0","setting.videocfg_fsr_detail":"0","setting.r_csgo_fsr_upsample":"0"},
    "competitive":{"setting.mat_vsync":"0","setting.msaa_samples":"2","setting.videocfg_texture_detail":"0","setting.r_texturefilteringquality":"0","setting.shaderquality":"0","setting.videocfg_particle_detail":"0","setting.videocfg_ao_detail":"0","setting.videocfg_fsr_detail":"0","setting.r_csgo_fsr_upsample":"0","setting.r_low_latency":"1"}}
prof("cs2","Counter-Strike 2","vac",True,"A",{"steam":"730","process":"cs2.exe"},{"steam":"730"},
     [{"id":"video","format":"keyvalues","path":"{steam}/userdata/*/730/local/cfg/cs2_video.txt","pick":"all"}],
     ["video:*defaultres*","video:*fullscreen*","video:*aspectratio*","video:*monitor*","video:*refreshrate*","video:*nowindowborder*","video:*brightness*"],
     {l:{v+k:val for k,val in d.items()} for l,d in cs.items()},SINGLE,None,
     {v+"setting.mat_vsync":note("Dikey eşitleme kapalı","V-Sync off","high","low"),v+"setting.msaa_samples":note("Kenar yumuşatma (rekabette 2x: kenarlar net)","MSAA (2x for competitive: clean edges)","medium","medium"),
      v+"setting.shaderquality":note("Gölgelendirici kalitesi","Shader detail","medium","low"),v+"setting.videocfg_ao_detail":note("Ortam gölgeleme kapalı","Ambient occlusion off","medium","low"),
      v+"setting.r_low_latency":note("NVIDIA Reflex açık","NVIDIA Reflex on","low","low")},
     {"tr":"Rekabet seviyesinde gölge kalitesine dokunulmaz: yüksek gölgeler rakibin gölgesini görmeni sağlar. Oyun kapalıyken uygula; CS2 açıkken dosyayı geri yazar.","en":"Competitive level leaves shadow quality alone: high shadows let you see enemy shadows. Apply while CS2 is closed; it rewrites the file on exit."})

# ---- Apex (KeyValues videoconfig.txt) ----
a="vc:VideoConfig/"
apx_low={"setting.mat_vsync_mode":"0","setting.mat_antialias_mode":"0","setting.ssao_enabled":"0","setting.shadow_enable":"0","setting.csm_enabled":"0","setting.r_decals":"0","setting.r_createmodeldecals":"0","setting.cl_ragdoll_maxcount":"0","setting.cl_gib_allow":"0","setting.particle_cpu_level":"0","setting.mat_forceaniso":"1","setting.r_lod_switch_scale":"0.5","setting.dvs_enable":"0"}
apx_bal={"setting.mat_vsync_mode":"0","setting.ssao_enabled":"0","setting.r_decals":"0","setting.cl_ragdoll_maxcount":"0","setting.dvs_enable":"0"}
prof("apex-legends","Apex Legends","eac",True,"A",{"steam":"1172470","process":"r5apex.exe,r5apex_dx12.exe"},{"steam":"1172470"},
     [{"id":"vc","format":"keyvalues","path":"%USERPROFILE%/Saved Games/Respawn/Apex/local/videoconfig.txt"}],
     ["vc:*defaultres*","vc:*fullscreen*","vc:*nowindowborder*","vc:*gamma*","vc:*monitor*","vc:*letterbox*","vc:*fov*"],
     {"quality":{a+"setting.mat_vsync_mode":"0","setting":None} and {a+"setting.mat_vsync_mode":"0"},"balanced":{a+k:v2 for k,v2 in apx_bal.items()},"maxfps":{a+k:v2 for k,v2 in apx_low.items()},"competitive":{a+k:v2 for k,v2 in apx_low.items()}},SINGLE,None,
     {a+"setting.mat_vsync_mode":note("Dikey eşitleme kapalı","V-Sync off","high","low"),a+"setting.shadow_enable":note("Güneş/dinamik gölgeler kapalı","Sun/dynamic shadows off","high","medium"),a+"setting.ssao_enabled":note("Ortam gölgeleme kapalı","Ambient occlusion off","medium","low"),a+"setting.dvs_enable":note("Uyarlanabilir çözünürlük kapalı (görüntü bulanıklaşmaz)","Adaptive resolution off (no blur)","low","medium")},
     {"tr":"FPS limitini Steam başlatma seçeneklerinden (+fps_max) ayarlayabilirsin. Çözünürlük, FOV ve tuşlara dokunulmadı.","en":"Set an FPS cap via Steam launch options (+fps_max). Resolution, FOV and keys were not touched."})

# ---- Minecraft Java (options.txt) ----
m="mc:"
mcl={"quality":{"enableVsync":"false","renderClouds":"\"fast\"","entityShadows":"true","renderDistance":"16","simulationDistance":"10"},
     "balanced":{"enableVsync":"false","renderDistance":"12","simulationDistance":"8","graphicsMode":"0","renderClouds":"\"fast\"","biomeBlendRadius":"1"},
     "maxfps":{"enableVsync":"false","renderDistance":"8","simulationDistance":"6","graphicsMode":"0","renderClouds":"\"false\"","entityShadows":"false","particles":"2","ao":"false","biomeBlendRadius":"0","mipmapLevels":"0","entityDistanceScaling":"0.75"},
     "competitive":{"enableVsync":"false","renderDistance":"10","simulationDistance":"6","graphicsMode":"0","renderClouds":"\"false\"","entityShadows":"false","particles":"2","ao":"false","biomeBlendRadius":"0"}}
prof("minecraft","Minecraft","none",False,"A",{"minecraft":"java","process":"javaw.exe"},{"minecraft":"java"},
     [{"id":"mc","format":"keyvalue","path":"{gameDir}/options.txt","separator":":"}],
     ["mc:key_*","mc:fov*","mc:gamma","mc:lang","mc:guiScale","mc:fullscreen*","mc:soundCategory*","mc:chat*","mc:skinCustomization*","mc:maxFps"],
     {l:{m+k:val for k,val in d.items()} for l,d in mcl.items()},NONCOMP_GPU|{"maxfps":{"radeonBoost":True},"competitive":{"radeonBoost":True}},None,
     {m+"renderDistance":note("Görüş mesafesi (yığın)","Render distance (chunks)","high","high"),m+"graphicsMode":note("Grafik: Hızlı","Graphics: Fast","medium","medium"),m+"enableVsync":note("Dikey eşitleme kapalı","V-Sync off","high","low"),m+"particles":note("Parçacıklar: en az","Particles: minimal","medium","low")},
     {"tr":"En büyük FPS artışı için Sodium modunu kur (Fabric). BoostRoot isterse kurulum sayfasını açar. Tuşlar, FOV ve ses ayarlarına dokunulmadı.","en":"For the biggest FPS gain install the Sodium mod (Fabric). Keys, FOV and sound were not touched."})

# ---- League of Legends (game.cfg) ----
g="cfg:"
lol={"quality":{"General/WaitForVerticalSync":"0"},
     "balanced":{"General/WaitForVerticalSync":"0","Performance/ShadowQuality":"2","Performance/EffectsQuality":"2","Performance/EnvironmentQuality":"2"},
     "maxfps":{"General/WaitForVerticalSync":"0","Performance/ShadowQuality":"0","Performance/EffectsQuality":"1","Performance/EnvironmentQuality":"1","Performance/CharacterQuality":"2","Performance/EnableHUDAnimations":"0","Performance/CharacterInking":"0","Performance/EnableFXAA":"0"},
     "competitive":{"General/WaitForVerticalSync":"0","Performance/ShadowQuality":"0","Performance/EnvironmentQuality":"1","Performance/EnableHUDAnimations":"0","Performance/CharacterInking":"0"}}
prof("league-of-legends","League of Legends","vanguard",True,"A",{"riot":"league_of_legends","process":"League of Legends.exe"},{"riot":"league_of_legends"},
     [{"id":"cfg","format":"ini","path":"{install}/Config/game.cfg"}],["cfg:*Width*","cfg:*Height*","cfg:*WindowMode*","cfg:*Cursor*","cfg:*Camera*","cfg:*Volume*","cfg:HUD/*","cfg:*Colorblind*","cfg:*FrameCap*"],
     {l:{g+k:val for k,val in d.items()} for l,d in lol.items()},SINGLE,None,
     {g+"General/WaitForVerticalSync":note("Dikey eşitleme kapalı","V-Sync off","high","low"),g+"Performance/ShadowQuality":note("Gölgeler kapalı","Shadows off","high","low"),g+"Performance/EnableHUDAnimations":note("Arayüz animasyonları kapalı","HUD animations off","low","low")},
     {"tr":"League bazen ayarları buluttan geri yükler; değişmediyse oyunu kapatıp tekrar Boost'la. FPS sınırını oyun içinden 'Sınırsız' yapma, monitör Hz'inin biraz üstünde tut.","en":"League may restore settings from the cloud; if nothing changed, close the game and boost again. Cap FPS a bit above your monitor's Hz in game."})

# ---- Rocket League (UE3 TASystemSettings.ini) ----
r="sys:SystemSettings/"
rl_off={k:"False" for k in ["DynamicShadows","MotionBlur","DepthOfField","Bloom","LensFlares","LightShafts","AmbientOcclusion","UseVsync"]}
prof("rocket-league","Rocket League","eac",True,"A",{"epic":"Sugar","steam":"252950","process":"RocketLeague.exe"},{"epic":"Sugar"},
     [{"id":"sys","format":"ini","path":"{documents}/My Games/Rocket League/TAGame/Config/TASystemSettings.ini"}],["sys:*Resolution*", "sys:*ResX*", "sys:*ResY*","sys:*Fullscreen*","sys:*Borderless*","sys:*Gamma*"],
     {"quality":{r+"UseVsync":"False","sys:SystemSettings/MotionBlur":"False"},"balanced":{r+k:"False" for k in ["MotionBlur","DepthOfField","LensFlares","UseVsync"]},"maxfps":{r+k:v2 for k,v2 in rl_off.items()},"competitive":{r+k:v2 for k,v2 in rl_off.items()}},SINGLE,None,
     {r+"DynamicShadows":note("Dinamik gölgeler kapalı","Dynamic shadows off","high","medium"),r+"MotionBlur":note("Hareket bulanıklığı kapalı","Motion blur off","low","low"),r+"UseVsync":note("Dikey eşitleme kapalı","V-Sync off","high","low")},
     {"tr":"Oyun içinde Kare Hızı sınırını monitörünün Hz değerinin üstüne al. Kamera ayarlarına dokunulmadı.","en":"In game set the frame rate cap above your monitor's Hz. Camera settings were not touched."})

# ---- GTA V (settings.xml) ----
x="xml:Settings/graphics/"
gta_low={"Tessellation":"0","ShadowQuality":"1","ReflectionQuality":"0","ReflectionMSAA":"0","SSAO":"0","MSAA":"0","ParticleQuality":"0","WaterQuality":"0","GrassQuality":"0","ShaderQuality":"0","PostFX":"0","Shadow_SoftShadows":"0","UltraShadows_Enabled":"false","DoF":"false","MotionBlurStrength":"0.000000"}
gta_bal={"Tessellation":"1","ReflectionMSAA":"0","GrassQuality":"1","PostFX":"1","DoF":"false","MotionBlurStrength":"0.000000","UltraShadows_Enabled":"false"}
prof("gta-v","Grand Theft Auto V","battleye",False,"A",{"steam":"271590","epic":"9d2d0eb64d5c44529cece33fe2a46482","process":"GTA5.exe,GTA5_Enhanced.exe"},{"steam":"271590"},
     [{"id":"xml","format":"xml","path":"{documents}/Rockstar Games/GTA V/settings.xml"}],["xml:Settings/video/*","xml:*Resolution*","xml:*Aspect*","xml:*Windowed*"],
     {"quality":{x+"MotionBlurStrength@value":"0.000000"},"balanced":{x+k+"@value":v2 for k,v2 in gta_bal.items()},"maxfps":{x+k+"@value":v2 for k,v2 in gta_low.items()},"competitive":{x+k+"@value":v2 for k,v2 in gta_low.items()}},NONCOMP_GPU,None,
     {x+"GrassQuality@value":note("Çimen kalitesi (en çok FPS yiyen)","Grass quality (biggest FPS cost)","high","medium"),x+"MSAA@value":note("MSAA kapalı","MSAA off","high","medium"),x+"PostFX@value":note("Son işleme efektleri","Post FX","medium","low")},
     {"tr":"Dikey eşitlemeyi oyun içinden kapat. Enhanced sürümde ayar dosyası farklıysa ayarları oyun içinden yap.","en":"Turn V-Sync off in game. If the Enhanced edition uses a different file, change settings in game."})

# ---- Group B: driver + Windows only, with a guide card ----
def b(id,name,ac,detect,launch,tips,comp=False):
    prof(id,name,ac,comp,"B",detect,launch,gpu=(SINGLE if comp else NONCOMP_GPU),tips=tips)
B=[
 ("roblox","Roblox","other",{"roblox":"player","process":"RobloxPlayerBeta.exe"},{"roblox":"player"},"Oyun içi Ayarlar > Grafik Modu: Manuel, kaliteyi 3-5 arası yap. FPS kilidini kaldırmak için güvenilmeyen 'fps unlocker' araçlarını kullanma; Roblox'un kendi 'Maksimum kare hızı' ayarını kullan.","In game Settings > Graphics Mode: Manual, quality 3-5. Use Roblox's own 'Maximum Frame Rate' setting, not third-party unlockers."),
 ("valheim","Valheim","none",{"steam":"892970","process":"valheim.exe"},{"steam":"892970"},"Grafik > Gölge kalitesi Orta, Vegetasyon Orta, Su yansıması kapalı, Bulut gölgeleri kapalı. Büyük üslerde FPS düşmesi normaldir.","Graphics > Shadow quality Medium, Vegetation Medium, water reflections off, cloud shadows off."),
 ("rust","Rust","eac",{"steam":"252490","process":"RustClient.exe"},{"steam":"252490"},"Seçenekler > Grafik: Kalite 2-3, Gölge kaskadı 1, Çimen gölgeleri kapalı, Ambient occlusion kapalı. F1 konsolu: 'gc.buffer 2048' (16 GB+ RAM).","Options > Graphics: Quality 2-3, shadow cascades 1, grass shadows off, AO off."),
 ("rainbow-six-siege","Rainbow Six Siege","battleye",{"steam":"359550","process":"RainbowSix.exe,RainbowSix_Vulkan.exe"},{"steam":"359550"},"Vulkan sürümünü dene (çoğu kartta daha fazla FPS). Doku Orta, Gölge Düşük (gölgeler rakibi gösterir, kapatma), Yansıma Düşük, Ambient occlusion kapalı.","Try the Vulkan build. Textures Medium, Shadows Low (keep on), Reflections Low, AO off."),
 ("7-days-to-die","7 Days to Die","eac",{"steam":"251570","process":"7DaysToDie.exe"},{"steam":"251570"},"Video > Gölge mesafesi Düşük, Ağaç kalitesi Orta, Çimen mesafesi Düşük, Hareket bulanıklığı kapalı, SSAO kapalı.","Video > Shadow distance Low, tree quality Medium, grass distance Low, motion blur off, SSAO off."),
 ("sons-of-the-forest","Sons of the Forest","none",{"steam":"1326470","process":"SonsOfTheForest.exe"},{"steam":"1326470"},"DLSS/FSR Kalite aç, Hacimsel bulutlar Düşük, Gölgeler Orta, Bitki örtüsü yoğunluğu Orta.","DLSS/FSR Quality on, volumetric clouds Low, shadows Medium, foliage density Medium."),
 ("stranded-deep","Stranded Deep","none",{"steam":"313120","process":"Stranded_Deep.exe"},{"steam":"313120"},"Su kalitesi Orta, Gölgeler Orta, Ambient occlusion kapalı.","Water quality Medium, shadows Medium, AO off."),
 ("peak","PEAK","none",{"steam":"3527290","name":"PEAK","process":"PEAK.exe"},{"steam":"3527290"},"Grafik ön ayarını Orta yap, Gölgeler Düşük, Render ölçeği %100.","Graphics preset Medium, shadows Low, render scale 100%."),
 ("lethal-company","Lethal Company","none",{"steam":"1966720","process":"Lethal Company.exe"},{"steam":"1966720"},"Oyun zaten hafif; FPS sınırını 'Sınırsız' yerine monitör Hz'ine yakın tut.","Already light; cap FPS near your monitor's Hz instead of unlimited."),
 ("phasmophobia","Phasmophobia","none",{"steam":"739630","process":"Phasmophobia.exe"},{"steam":"739630"},"Anti-aliasing SMAA, Doku Yarım, Gölge Orta, Ambient occlusion kapalı, Bloom kapalı.","AA SMAA, textures Half, shadows Medium, AO off, bloom off."),
 ("the-last-of-us-part-1","The Last of Us Part I","none",{"steam":"1888930","process":"tlou-i.exe"},{"steam":"1888930"},"VRAM çubuğunu aşma: Doku Orta/Yüksek, Ekran uzayı yansımaları kapalı, FSR/DLSS Kalite.","Stay under the VRAM bar: textures Medium/High, SSR off, FSR/DLSS Quality."),
 ("the-last-of-us-part-2","The Last of Us Part II","none",{"steam":"2531310","process":"tlou-ii.exe"},{"steam":"2531310"},"FSR/DLSS Kalite, Hacimsel efektler Orta, Gölgeler Orta.","FSR/DLSS Quality, volumetrics Medium, shadows Medium."),
 ("red-dead-redemption-2","Red Dead Redemption 2","other",{"steam":"1174180","process":"RDR2.exe"},{"steam":"1174180"},"API: Vulkan. Hacimsel aydınlatma Orta, Su fiziği kalitesi en düşük, Ağaç tesselasyonu kapalı, Çimen detayı Orta, MSAA kapalı (TAA Orta).","API Vulkan. Volumetric lighting Medium, water physics lowest, tree tessellation off, grass Medium, MSAA off (TAA Medium)."),
 ("cyberpunk-2077","Cyberpunk 2077","none",{"steam":"1091500","epic":"Ginger","process":"Cyberpunk2077.exe"},{"steam":"1091500"},"Işın izleme kapalı, FSR/DLSS Kalite, Kalabalık yoğunluğu Orta, Hacimsel sis Orta, Ekran uzayı yansımaları Orta.","Ray tracing off, FSR/DLSS Quality, crowd density Medium, volumetric fog Medium, SSR Medium."),
 ("elden-ring","Elden Ring","eac",{"steam":"1245620","process":"eldenring.exe"},{"steam":"1245620"},"Işın izleme kapalı, Gölge Orta, Ambient occlusion Orta, Hareket bulanıklığı kapalı. Oyun 60 FPS ile sınırlıdır.","Ray tracing off, shadows Medium, AO Medium, motion blur off. The game is capped at 60 FPS."),
 ("euro-truck-simulator-2","Euro Truck Simulator 2","none",{"steam":"227300","process":"eurotrucks2.exe"},{"steam":"227300"},"Ölçekleme %100, Gölge Orta, Yansımalar Düşük, Anizotropik 8x. Ağır trafik modları FPS düşürür.","Scaling 100%, shadows Medium, reflections Low, anisotropic 8x."),
 ("forza-horizon-5","Forza Horizon 5","other",{"steam":"1551360","process":"ForzaHorizon5.exe"},{"steam":"1551360"},"Oyunun kendi ön ayarını kullan, Çevre doku Yüksek, Ray tracing kapalı, FSR/DLSS Kalite, Dinamik çözünürlük kapalı.","Use the game's preset, ray tracing off, FSR/DLSS Quality, dynamic resolution off."),
 ("ranch-simulator","Ranch Simulator","none",{"steam":"1119730","name":"Ranch Simulator","process":"RanchSimulator-Win64-Shipping.exe,Ranch_Simulator-Win64-Shipping.exe"},{"steam":"1119730"},"Bitki örtüsü Orta, Gölgeler Orta, FSR/DLSS Kalite.","Foliage Medium, shadows Medium, FSR/DLSS Quality."),
 ("banker-simulator","Banker Simulator","none",{"name":"Banker Simulator","process":"BankerSimulator.exe"},{"name":"Banker Simulator"},"Hafif bir oyun; Gölgeleri Orta, V-Sync kapalı yap.","Light game; shadows Medium, V-Sync off."),
]
for id,name,ac,det,la,tr,en in B: b(id,name,ac,det,la,{"tr":tr,"en":en}, comp=id in ("rainbow-six-siege","rust"))
