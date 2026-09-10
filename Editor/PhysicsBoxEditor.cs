using UnityEditor;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    [CustomEditor(typeof(PhysicsBox))]
    public class PhysicsBoxEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
        }

        private void OnSceneGUI()
        {
            PhysicsBox physicsBox = (PhysicsBox)target;

            // Convert the local center to world space for the handle
            Vector3 worldCenter = physicsBox.transform.TransformPoint(physicsBox.Center);

            // Calculate the positions of the dot handles
            Vector3 handle1Pos = worldCenter + new Vector3(physicsBox.Size.x / 2, 0, 0);
            Vector3 handle2Pos = worldCenter - new Vector3(physicsBox.Size.x / 2, 0, 0);
            Vector3 handle3Pos = worldCenter + new Vector3(0, physicsBox.Size.y / 2, 0);
            Vector3 handle4Pos = worldCenter - new Vector3(0, physicsBox.Size.y / 2, 0);

            float handleSize = .5f; // Adjust the size to your preference
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            int controlId2 = GUIUtility.GetControlID(FocusType.Passive);
            int controlId3 = GUIUtility.GetControlID(FocusType.Passive);
            int controlId4 = GUIUtility.GetControlID(FocusType.Passive);

            EditorGUI.BeginChangeCheck();

            // Draw the dot handles and allow for manipulation
            handle1Pos = Handles.FreeMoveHandle(
                controlId,
                handle1Pos,
                handleSize,
                Vector3.zero,
                Handles.DotHandleCap
            );
            handle2Pos = Handles.FreeMoveHandle(
                controlId2,
                handle2Pos,
                handleSize,
                Vector3.zero,
                Handles.DotHandleCap
            );
            handle3Pos = Handles.FreeMoveHandle(
                controlId3,
                handle3Pos,
                handleSize,
                Vector3.zero,
                Handles.DotHandleCap
            );
            handle4Pos = Handles.FreeMoveHandle(
                controlId4,
                handle4Pos,
                handleSize,
                Vector3.zero,
                Handles.DotHandleCap
            );

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(physicsBox, "Modify Hurtbox");

                // Round the handle positions to snap to rounded numbers
                handle1Pos = RoundVector(handle1Pos);
                handle2Pos = RoundVector(handle2Pos);
                handle3Pos = RoundVector(handle3Pos);
                handle4Pos = RoundVector(handle4Pos);

                // Update the size based on handle positions, ensuring it remains positive
                physicsBox.Size = new Vector2(
                    Mathf.Abs(handle1Pos.x - handle2Pos.x),
                    Mathf.Abs(handle3Pos.y - handle4Pos.y)
                );

                // Update the center based on the average of the handle positions
                physicsBox.Center = physicsBox.transform.InverseTransformPoint(
                    new Vector3(
                        (handle1Pos.x + handle2Pos.x) / 2,
                        (handle3Pos.y + handle4Pos.y) / 2,
                        worldCenter.z // Assuming Hurtbox is 2D and z-value is constant
                    )
                );
            }
        }

        private Vector3 RoundVector(Vector3 vector)
        {
            return new Vector3(Mathf.Round(vector.x), Mathf.Round(vector.y), Mathf.Round(vector.z));
        }
    }
}
