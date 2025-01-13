using BepInEx;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Unity.Netcode;
using BepInEx.Logging;
using GameNetcodeStuff;

namespace LCPointPet {
    [BepInPlugin(mod_guid, mod_name, mod_version)]
    public class LCPointPetBase : BaseUnityPlugin {
        private const string mod_guid = "raptureawaits.pointpet";
		private const string mod_name = "Can I Pet That Dog?";
		private const string mod_version = "1.0.0";
        

        internal static LCPointPetBase instance;
        internal static ManualLogSource modlog;

        private readonly Harmony harmony = new(mod_guid);

        internal static AssetBundle new_sounds;
        private AudioClip[] clips;
        internal System.Random rand;
        public static float volume = 1.0f;
        public static int point_mask = 524288;
        public static string[] dog_collider_names = {"MouthDogModel", "MouthDog(Clone)"};

        private int _last_played;
        public int last_played {
			get { return _last_played; }
			set { _last_played = value; }
		}

        public TimeSpan cooldown_seconds = new TimeSpan(0, 0, 3);
        public Dictionary<ulong, DateTime> cooldowns = new Dictionary<ulong, DateTime>();

        private void Awake() {
            if (instance == null) {
                instance = this;
            }
            modlog = BepInEx.Logging.Logger.CreateLogSource("PointPet");

            string mod_dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            new_sounds = AssetBundle.LoadFromFile(Path.Combine(mod_dir, "pointpet"));
            if (new_sounds == null) {
                modlog.LogError("Failed to load AssetBundle.");
                return;
            }

            clips = new_sounds.LoadAllAssets<AudioClip>();
            if (clips == null || clips.Length == 0) {
                modlog.LogError("Failed to load audio data from extracted assets.");
            }

            rand = new System.Random();

            harmony.PatchAll();
            modlog.LogInfo($"Plugin {mod_guid} is loaded!");
        }

        public AudioClip getRandomClip() {
            int num = last_played;
            while (num == last_played) {
                num = rand.Next(clips.Length);
            }
            last_played = num;
            return clips[num];
        }
    }
}

namespace LCPointPet.Patches {
    [HarmonyPatch(typeof(PlayerControllerB))]
    internal class PointPetPatch {
        internal static ManualLogSource modlog = LCPointPetBase.modlog;
		internal static LCPointPetBase b = LCPointPetBase.instance;

        [HarmonyPatch("PerformEmote")]
        [HarmonyPostfix]
        static void PerformEmotePostfix(int emoteID, PlayerControllerB __instance, ref bool ___performingEmote, ref AudioSource ___itemAudio, ref ulong ___playerClientId) {
            ulong pid = ___playerClientId;

            bool is_on_cooldown = b.cooldowns.ContainsKey(pid);
			// Check if the player's audio cooldown has expired, and remove the player from the dict if it has
			if (is_on_cooldown && DateTime.Now > b.cooldowns[pid]) {
				b.cooldowns.Remove(pid);
				is_on_cooldown = false;
			}

            bool is_point_player = __instance.IsOwner && __instance.isPlayerControlled;
            if (emoteID == 2 && is_point_player && !is_on_cooldown) {
                RaycastHit hit_collider;
                Vector3  pos = __instance.gameplayCamera.transform.position;
                Vector3 forward = __instance.gameplayCamera.transform.forward;
                
                if (Physics.Raycast(pos, forward, out hit_collider, Mathf.Infinity, layerMask: LCPointPetBase.point_mask)) {
                    if (Array.Exists(LCPointPetBase.dog_collider_names, element => element == hit_collider.collider.name))  {
                        modlog.LogInfo($"[{__instance.OwnerClientId}] Can I pet that dog?");
                        ___itemAudio.PlayOneShot(b.getRandomClip(), LCPointPetBase.volume);
                        b.cooldowns.Add(___playerClientId, DateTime.Now + b.cooldown_seconds);
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(StartOfRound))]
	internal class CooldownResetPatch {
		internal static ManualLogSource modlog = LCPointPetBase.modlog;
		internal static LCPointPetBase b = LCPointPetBase.instance;

		[HarmonyPatch("Start")]
		[HarmonyPostfix]
		static void StartPostfix() {
			b.cooldowns.Clear();
		}
	}
}