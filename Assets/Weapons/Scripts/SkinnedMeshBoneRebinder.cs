using System.Collections.Generic;
using UnityEngine;

namespace Weapons
{
	// Привязывает скинированную модель (например, оружие) к костям ДРУГОГО скелета с теми же именами —
	// к риге рук, в котором уже есть кости оружия (w_base, slide_bone, mag_bone...) и которые анимирует клип.
	// Свой скелет модели оружия после этого не используется: модель деформируется костями рук.
	//
	// Зачем: один Animator на модели рук анимирует и руки, и оружие; модели в префабе остаются связанными
	// с FBX (без Unpack), переэкспорт подхватывается сам. Стандартный приём для модульных персонажей (одежда на общем скелете).
	//
	// Вешается на корень модели оружия внутри префаба. Привязка делается при старте; в редакторе её можно
	// сделать заранее кнопкой "Привязать к скелету" в меню компонента (⋮) — тогда модель видна на месте и в префабе.
	public class SkinnedMeshBoneRebinder : MonoBehaviour
	{
		[Tooltip("Корень скелета, к которому привязать модель — экземпляр модели рук (FPHandsRig) или его кость root. " +
			"Кости ищутся в нём по именам")]
		[SerializeField] private Transform targetSkeleton;
		[Tooltip("Выключать собственный скелет модели после привязки — он больше не нужен и не должен мешать")]
		[SerializeField] private bool disableOwnSkeleton = true;

		private void Awake()
		{
			Rebind();
		}

		[ContextMenu("Привязать к скелету")]
		public void Rebind()
		{
			if (targetSkeleton == null)
			{
				Debug.LogWarning($"{name}: у SkinnedMeshBoneRebinder не назначен Target Skeleton", this);
				return;
			}

			Dictionary<string, Transform> targetBones = new Dictionary<string, Transform>();
			CollectBones(targetSkeleton, targetBones);

			HashSet<Transform> ownBones = new HashSet<Transform>();
			foreach (SkinnedMeshRenderer skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				Transform[] bones = skin.bones;
				int missing = 0;
				for (int i = 0; i < bones.Length; i++)
				{
					if (bones[i] == null) continue;
					if (targetBones.TryGetValue(bones[i].name, out Transform target))
					{
						if (bones[i] != target && bones[i].IsChildOf(transform)) ownBones.Add(bones[i]);
						bones[i] = target;
					}
					else
					{
						missing++;
					}
				}
				skin.bones = bones;

				if (skin.rootBone != null && targetBones.TryGetValue(skin.rootBone.name, out Transform targetRoot))
					skin.rootBone = targetRoot;

				if (missing > 0)
					Debug.LogWarning($"{name}: у {skin.name} {missing} кост(и/ей) не найдено в {targetSkeleton.name} — " +
						"они остаются на своём скелете и не будут анимироваться", this);
			}

			// собственный скелет больше не нужен — выключаем его верхние кости (то, что лежит прямо под моделью)
			if (!disableOwnSkeleton) return;
			foreach (Transform bone in ownBones)
			{
				Transform top = bone;
				while (top.parent != null && top.parent != transform) top = top.parent;
				if (top.parent == transform && top.GetComponentInChildren<SkinnedMeshRenderer>(true) == null)
					top.gameObject.SetActive(false);
			}
		}

		private static void CollectBones(Transform root, Dictionary<string, Transform> result)
		{
			if (!result.ContainsKey(root.name)) result.Add(root.name, root);
			foreach (Transform child in root) CollectBones(child, result);
		}
	}
}
