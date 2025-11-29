using UnityEngine;
using System.Collections.Generic;
using System.Reflection;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MagicaClothTools
{
    /// <summary>
    /// Converts between MagicaCloth 1 and MagicaCloth 2 capsule colliders with proper scaling translation.
    /// Handles collision geometry differences, radius ordering, and center offset for perfect conversion.
    /// 
    /// Extended features:
    /// - Convert colliders on children
    /// - Replace old collider or keep both
    /// - Place translated collider on same object or on a new child GameObject
    /// </summary>
    [AddComponentMenu("MagicaCloth Tools/Capsule Collider Converter")]
    public class MagicaCapsuleColliderConverter : MonoBehaviour
    {
        public enum ConvertScope
        {
            ThisGameObjectOnly,
            ThisAndChildren
        }

        public enum ConvertMode
        {
            TranslateAndOverride, // Replace old collider type
            TranslateAndKeepBoth  // Keep old collider and add new type
        }

        public enum PlacementMode
        {
            SameGameObject,
            NewChildGameObject
        }

        [Header("Scope")]
        public ConvertScope scope = ConvertScope.ThisAndChildren; // DEFAULT: this + children
        public bool includeInactiveChildren = true;

        [Header("Behavior")]
        public ConvertMode mode = ConvertMode.TranslateAndKeepBoth; // DEFAULT: keep both

        [Header("Placement")]
        public PlacementMode placement = PlacementMode.NewChildGameObject; // DEFAULT: new child GO
        public string newChildNameSuffix = "_ConvertedCollider";

#if UNITY_EDITOR
        [CustomEditor(typeof(MagicaCapsuleColliderConverter))]
        public class ConverterEditor : Editor
        {
            public override void OnInspectorGUI()
            {
                DrawDefaultInspector();

                MagicaCapsuleColliderConverter converter = (MagicaCapsuleColliderConverter)target;

                GUILayout.Space(10);

                if (GUILayout.Button("Convert Collider(s)", GUILayout.Height(35)))
                {
                    converter.Convert();
                }

                EditorGUILayout.HelpBox(
                    "Automatically detects MagicaCloth 1 or 2 capsule colliders and converts to the opposite system.\n\n" +
                    "• Handles size scaling differences\n" +
                    "• Corrects radius ordering\n" +
                    "• Applies center offset for perfect alignment\n\n" +
                    "Extended:\n" +
                    "• Can process children\n" +
                    "• Replace old or keep both\n" +
                    "• Place on same GameObject or new child",
                    MessageType.Info
                );
            }
        }
#endif

        /// <summary>
        /// Converts capsule colliders based on scope/mode/placement.
        /// Per collider, detects which system is present and converts to the other.
        /// </summary>
        public void Convert()
        {
            var targets = CollectTargets();

            if (targets.Count == 0)
            {
                Debug.LogWarning("No MagicaCloth 1 or 2 capsule colliders found in scope.", this);
                return;
            }

#if UNITY_EDITOR
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Convert Magica Capsule Colliders");
#endif

            int converted = 0;
            foreach (var t in targets)
            {
                if (ConvertOne(t))
                    converted++;
            }

#if UNITY_EDITOR
            Undo.CollapseUndoOperations(undoGroup);
#endif

            Debug.Log($"Converted {converted} collider(s).", this);
        }

        // ------------------------------------------------------------
        // Target collection
        // ------------------------------------------------------------

        private struct TargetCollider
        {
            public GameObject go;
            public MagicaCloth.MagicaCapsuleCollider magica1;
            public MagicaCloth2.MagicaCapsuleCollider magica2;
        }

        private List<TargetCollider> CollectTargets()
        {
            var list = new List<TargetCollider>();

            if (scope == ConvertScope.ThisGameObjectOnly)
            {
                AddTargetsOnGO(gameObject, list);
            }
            else
            {
                var transforms = GetComponentsInChildren<Transform>(includeInactiveChildren);
                foreach (var tr in transforms)
                    AddTargetsOnGO(tr.gameObject, list);
            }

            return list;
        }

        private void AddTargetsOnGO(GameObject go, List<TargetCollider> list)
        {
            var m1 = go.GetComponent<MagicaCloth.MagicaCapsuleCollider>();
            var m2 = go.GetComponent<MagicaCloth2.MagicaCapsuleCollider>();

            // If neither present, ignore
            if (!m1 && !m2) return;

            // If both present, ignore this object to avoid ambiguity
            if (m1 && m2)
            {
                Debug.LogWarning($"Skipping '{go.name}' because both MagicaCloth 1 and 2 colliders are present.", go);
                return;
            }

            list.Add(new TargetCollider { go = go, magica1 = m1, magica2 = m2 });
        }

        // ------------------------------------------------------------
        // Conversion orchestration
        // ------------------------------------------------------------

        private bool ConvertOne(TargetCollider target)
        {
            if (target.magica1)
            {
                var destGO = ResolveDestinationGO(target.go, "MC2");
                var newCol = ConvertMagica1ToMagica2(target.magica1, destGO);

                if (newCol == null) return false;

                if (mode == ConvertMode.TranslateAndOverride)
                {
#if UNITY_EDITOR
                    Undo.DestroyObjectImmediate(target.magica1);
#else
                    Destroy(target.magica1);
#endif
                }

                return true;
            }
            else if (target.magica2)
            {
                var destGO = ResolveDestinationGO(target.go, "MC1");
                var newCol = ConvertMagica2ToMagica1(target.magica2, destGO);

                if (newCol == null) return false;

                if (mode == ConvertMode.TranslateAndOverride)
                {
#if UNITY_EDITOR
                    Undo.DestroyObjectImmediate(target.magica2);
#else
                    Destroy(target.magica2);
#endif
                }

                return true;
            }

            return false;
        }

        private GameObject ResolveDestinationGO(GameObject sourceGO, string tag)
        {
            if (placement == PlacementMode.SameGameObject)
                return sourceGO;

            // Create a new child under sourceGO
            var childName = sourceGO.name + newChildNameSuffix + "_" + tag;
            var child = new GameObject(childName);
#if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(child, "Create Converted Collider Object");
#endif
            child.transform.SetParent(sourceGO.transform, false);
            child.transform.localPosition = Vector3.zero;
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = Vector3.one;

            return child;
        }

        // ------------------------------------------------------------
        // Actual conversion logic (same math as original)
        // ------------------------------------------------------------

        /// <summary>
        /// Converts MagicaCloth 1 capsule collider to MagicaCloth 2 on destGO.
        /// Returns the new collider.
        /// </summary>
        private MagicaCloth2.MagicaCapsuleCollider ConvertMagica1ToMagica2(
            MagicaCloth.MagicaCapsuleCollider magica1,
            GameObject destGO)
        {
            Vector3 originalCenter = GetCenterValue(magica1);

#if UNITY_EDITOR
            var magica2 = Undo.AddComponent<MagicaCloth2.MagicaCapsuleCollider>(destGO);
#else
            var magica2 = destGO.AddComponent<MagicaCloth2.MagicaCapsuleCollider>();
#endif

            magica2.direction = (MagicaCloth2.MagicaCapsuleCollider.Direction)magica1.AxisMode;

            float startRadius = magica1.EndRadius;   // Reversed
            float endRadius   = magica1.StartRadius; // Reversed

            float length = 2f * magica1.Length + magica1.StartRadius + magica1.EndRadius;

            magica2.SetSize(startRadius, endRadius, length);
            magica2.alignedOnCenter = true;
            magica2.reverseDirection = false;

            if (magica1.StartRadius != magica1.EndRadius)
            {
                float centerOffsetMagnitude = (magica1.EndRadius - magica1.StartRadius) * 0.5f;
                Vector3 localDirection = GetLocalDirection(magica1.AxisMode);
                Vector3 centerOffset = localDirection * centerOffsetMagnitude;
                SetCenterValue(magica2, originalCenter + centerOffset);
            }
            else
            {
                SetCenterValue(magica2, originalCenter);
            }

            return magica2;
        }

        /// <summary>
        /// Converts MagicaCloth 2 capsule collider to MagicaCloth 1 on destGO.
        /// Returns the new collider.
        /// </summary>
        private MagicaCloth.MagicaCapsuleCollider ConvertMagica2ToMagica1(
            MagicaCloth2.MagicaCapsuleCollider magica2,
            GameObject destGO)
        {
            Vector3 originalCenter = GetCenterValue(magica2);

            Vector3 size = magica2.GetSize();

#if UNITY_EDITOR
            var magica1 = Undo.AddComponent<MagicaCloth.MagicaCapsuleCollider>(destGO);
#else
            var magica1 = destGO.AddComponent<MagicaCloth.MagicaCapsuleCollider>();
#endif

            magica1.AxisMode = (MagicaCloth.MagicaCapsuleCollider.Axis)magica2.direction;

            float startRadius = size.y; // Reversed
            float endRadius   = size.x; // Reversed

            float length = Mathf.Max((size.z - size.x - size.y) / 2f, 0.001f);

            magica1.StartRadius = startRadius;
            magica1.EndRadius   = endRadius;
            magica1.Length      = length;

            if (size.x != size.y)
            {
                float centerOffsetMagnitude = -(size.x - size.y) * 0.5f;
                Vector3 localDirection = GetLocalDirection(magica1.AxisMode);
                Vector3 centerOffset = localDirection * centerOffsetMagnitude;
                SetCenterValue(magica1, originalCenter + centerOffset);
            }
            else
            {
                SetCenterValue(magica1, originalCenter);
            }

            return magica1;
        }

        // ------------------------------------------------------------
        // Center reflection/serialized access
        // ------------------------------------------------------------

        private Vector3 GetCenterValue(Component component)
        {
            try
            {
#if UNITY_EDITOR
                SerializedObject serializedObject = new SerializedObject(component);
                SerializedProperty centerProperty = serializedObject.FindProperty("center");
                if (centerProperty != null)
                    return centerProperty.vector3Value;
#endif

                System.Type currentType = component.GetType();
                while (currentType != null)
                {
                    var centerField = currentType.GetField("center", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (centerField != null)
                        return (Vector3)centerField.GetValue(component);

                    currentType = currentType.BaseType;
                }
            }
            catch { }

            return Vector3.zero;
        }

        private void SetCenterValue(Component component, Vector3 center)
        {
            try
            {
#if UNITY_EDITOR
                SerializedObject serializedObject = new SerializedObject(component);
                SerializedProperty centerProperty = serializedObject.FindProperty("center");
                if (centerProperty != null)
                {
                    centerProperty.vector3Value = center;
                    serializedObject.ApplyModifiedProperties();
                    return;
                }
#endif

                System.Type currentType = component.GetType();
                while (currentType != null)
                {
                    var centerField = currentType.GetField("center", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (centerField != null)
                    {
                        centerField.SetValue(component, center);
                        return;
                    }
                    currentType = currentType.BaseType;
                }
            }
            catch { }
        }

        private Vector3 GetLocalDirection(MagicaCloth.MagicaCapsuleCollider.Axis axis)
        {
            switch (axis)
            {
                case MagicaCloth.MagicaCapsuleCollider.Axis.X: return Vector3.right;
                case MagicaCloth.MagicaCapsuleCollider.Axis.Y: return Vector3.up;
                case MagicaCloth.MagicaCapsuleCollider.Axis.Z: return Vector3.forward;
                default: return Vector3.right;
            }
        }
    }
}
