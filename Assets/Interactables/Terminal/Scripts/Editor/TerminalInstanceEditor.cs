using UnityEditor;
using UnityEngine;

namespace Interactables.EditorTools
{
	// Кнопка "Fit UI Camera To Canvas" под полями TerminalInstance. Лежит в папке Editor —
	// в сборку игры не попадает.
	[CustomEditor(typeof(TerminalInstance))]
	public class TerminalInstanceEditor : UnityEditor.Editor
	{
		public override void OnInspectorGUI()
		{
			DrawDefaultInspector();

			EditorGUILayout.Space();
			if (!GUILayout.Button("Fit UI Camera To Canvas", GUILayout.Height(26))) return;

			var instance = (TerminalInstance)target;
			Camera camera = instance.UICamera;
			Canvas canvas = instance.TerminalCanvas;
			if (camera == null || canvas == null)
			{
				Debug.LogWarning("TerminalInstance: не найдены UICamera или TerminalCanvas среди дочерних объектов", instance);
				return;
			}

			// Undo + пометка изменений: без этого новая позиция камеры не сохранилась бы в сцене/префабе
			Undo.RecordObjects(new Object[] { camera.transform, camera, canvas }, "Fit UI Camera To Canvas");
			instance.FitCameraToCanvas();

			PrefabUtility.RecordPrefabInstancePropertyModifications(camera.transform);
			PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
			PrefabUtility.RecordPrefabInstancePropertyModifications(canvas);
			EditorUtility.SetDirty(camera);
		}
	}
}
