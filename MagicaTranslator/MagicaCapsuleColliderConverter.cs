using UnityEngine;
using System.Reflection;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MagicaClothTools
{
    /// <summary>
    /// Converts between MagicaCloth 1 and MagicaCloth 2 capsule colliders with proper scaling translation.
    /// Handles collision geometry differences, radius ordering, and center offset for perfect conversion.
    /// </summary>
    [AddComponentMenu("MagicaCloth Tools/Capsule Collider Converter")]
    public class MagicaCapsuleColliderConverter : MonoBehaviour
    {
#if UNITY_EDITOR
        [CustomEditor(typeof(MagicaCapsuleColliderConverter))]
        public class ConverterEditor : Editor
        {
            public override void OnInspectorGUI()
            {
                DrawDefaultInspector();
                
                MagicaCapsuleColliderConverter converter = (MagicaCapsuleColliderConverter)target;
                
                GUILayout.Space(10);
                
                if (GUILayout.Button("Convert Collider", GUILayout.Height(35)))
                {
                    converter.Convert();
                }
                
                EditorGUILayout.HelpBox(
                    "Automatically detects MagicaCloth 1 or 2 capsule colliders and converts to the opposite system.\n\n" +
                    "• Handles size scaling differences\n" +
                    "• Corrects radius ordering\n" +
                    "• Applies center offset for perfect alignment",
                    MessageType.Info
                );
            }
        }
#endif

        /// <summary>
        /// Converts between MagicaCloth 1 and MagicaCloth 2 capsule colliders.
        /// Automatically detects which system is present and converts to the other.
        /// </summary>
        public void Convert()
        {
            var magica1 = GetComponent<MagicaCloth.MagicaCapsuleCollider>();
            var magica2 = GetComponent<MagicaCloth2.MagicaCapsuleCollider>();

            // Validation
            if (magica1 && magica2)
            {
                Debug.LogWarning("Both MagicaCloth 1 and 2 colliders are present. Please remove one before converting.", this);
                return;
            }

            if (!magica1 && !magica2)
            {
                Debug.LogWarning("No MagicaCloth capsule collider found on this GameObject.", this);
                return;
            }

            // Perform conversion
            if (magica1)
            {
                ConvertMagica1ToMagica2(magica1);
                Debug.Log("Successfully converted MagicaCloth 1 → MagicaCloth 2", this);
            }
            else
            {
                ConvertMagica2ToMagica1(magica2);
                Debug.Log("Successfully converted MagicaCloth 2 → MagicaCloth 1", this);
            }
        }

        /// <summary>
        /// Converts MagicaCloth 1 capsule collider to MagicaCloth 2.
        /// </summary>
        private void ConvertMagica1ToMagica2(MagicaCloth.MagicaCapsuleCollider magica1)
        {
            // Store original center for offset calculation
            Vector3 originalCenter = GetCenterValue(magica1);
            
            // Create new MagicaCloth 2 component
            var magica2 = gameObject.AddComponent<MagicaCloth2.MagicaCapsuleCollider>();
            
            // Convert direction/axis
            magica2.direction = (MagicaCloth2.MagicaCapsuleCollider.Direction)magica1.AxisMode;
            
            // Convert size values with radius reversal
            // Note: Magica2 has reversed start/end radius compared to Magica1
            float startRadius = magica1.EndRadius;   // Reversed
            float endRadius = magica1.StartRadius;   // Reversed
            
            // Convert length from Magica1's half-length to Magica2's full collision length
            // Formula: 2 * Length + StartRadius + EndRadius (accounts for hemispherical caps)
            float length = 2f * magica1.Length + magica1.StartRadius + magica1.EndRadius;
            
            // Apply size conversion
            magica2.SetSize(startRadius, endRadius, length);
            magica2.alignedOnCenter = true;
            magica2.reverseDirection = false;
            
            // Apply center offset for asymmetric capsules
            // When start/end radii differ, collision volumes need center alignment
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
        }

        /// <summary>
        /// Converts MagicaCloth 2 capsule collider to MagicaCloth 1.
        /// </summary>
        private void ConvertMagica2ToMagica1(MagicaCloth2.MagicaCapsuleCollider magica2)
        {
            // Store original center for offset calculation
            Vector3 originalCenter = GetCenterValue(magica2);
            
            // Get size values from MagicaCloth 2
            Vector3 size = magica2.GetSize();
            
            // Create new MagicaCloth 1 component
            var magica1 = gameObject.AddComponent<MagicaCloth.MagicaCapsuleCollider>();
            
            // Convert direction/axis
            magica1.AxisMode = (MagicaCloth.MagicaCapsuleCollider.Axis)magica2.direction;
            
            // Convert size values with radius reversal
            // Note: Magica2 has reversed start/end radius compared to Magica1
            float startRadius = size.y;   // Reversed
            float endRadius = size.x;     // Reversed
            
            // Convert length from Magica2's full collision length to Magica1's half-length
            // Reverse formula: (fullLength - caps) / 2 = halfLength
            float length = Mathf.Max((size.z - size.x - size.y) / 2f, 0.001f);
            
            // Apply size conversion
            magica1.StartRadius = startRadius;
            magica1.EndRadius = endRadius;
            magica1.Length = length;
            
            // Apply center offset compensation for asymmetric capsules
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
        }

        /// <summary>
        /// Gets the center value from a collider component using multiple access methods.
        /// </summary>
        private Vector3 GetCenterValue(Component component)
        {
            try
            {
                // Try SerializedObject approach (most reliable for Unity components)
#if UNITY_EDITOR
                SerializedObject serializedObject = new SerializedObject(component);
                SerializedProperty centerProperty = serializedObject.FindProperty("center");
                if (centerProperty != null)
                {
                    return centerProperty.vector3Value;
                }
#endif

                // Try reflection on base classes
                System.Type currentType = component.GetType();
                while (currentType != null)
                {
                    var centerField = currentType.GetField("center", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (centerField != null)
                    {
                        return (Vector3)centerField.GetValue(component);
                    }
                    currentType = currentType.BaseType;
                }
            }
            catch (System.Exception)
            {
                // Fallback to zero if access fails
            }

            return Vector3.zero;
        }

        /// <summary>
        /// Sets the center value on a collider component using multiple access methods.
        /// </summary>
        private void SetCenterValue(Component component, Vector3 center)
        {
            try
            {
                // Try SerializedObject approach (most reliable for Unity components)
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

                // Try reflection on base classes
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
            catch (System.Exception)
            {
                // Silent fail - center offset is optional enhancement
            }
        }

        /// <summary>
        /// Gets the local direction vector for the given axis.
        /// </summary>
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
