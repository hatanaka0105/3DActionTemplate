using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using UnityEngine;

    public class PathList : MonoBehaviour
    {
        [SerializeField]
        private List<Transform> _transformList;

        [SerializeField]
        private Color _color;

        [SerializeField]
        private Transform _standbyPos;

        private List<Vector3> _posList;

        public bool IsDoor = false;
        public Vector3 GetStandbyPos
        {
            get
            {
                if (_standbyPos == null)
                {
                    return Vector3.zero;
                }
                return _standbyPos.position;
            }
        }

        [SerializeField, HideInInspector]
        private int _id;
        public int Id { get { return _id; } }

        private bool _isFastRespawn = false;
        public bool IsFastRespawn { get => _isFastRespawn; }

        public void InitializeId(int id)
        {
            _id = id;
        }

        public Vector3[] GetPath()
        {
            _posList = new List<Vector3>(_transformList.Count);
            foreach (var trans in _transformList)
            {
                _posList.Add(trans.position);
            }

            if (_posList != null)
            {
                return _posList.ToArray();
            }
            return null;
        }

        public List<Transform> GetPathTransformList()
        {
            return _transformList;
        }

        private void UpdatePath()
        {
            _posList = new List<Vector3>(_transformList.Count);
            foreach (var trans in _transformList)
            {
                _posList.Add(trans.position);
            }
        }

        private Vector3 GetStartPos()
        {
            return _transformList[0].position;
        }

        public void EnableFastRespawn()
        {
            _isFastRespawn = true;
        }

        public Transform GetLastPos()
        {
            if (_transformList == null || _transformList.Count == 0)
            {
                return null;
            }
            return _transformList[_transformList.Count - 1];
        }

#if UNITY_EDITOR

        public static Vector3 CatmullRom(Vector3[] points, int index, float t)
        {
            // Ensure the index is within bounds
            index = Mathf.Clamp(index, 0, points.Length - 2);

            Vector3 p0 = points[Mathf.Clamp(index - 1, 0, points.Length - 1)];
            Vector3 p1 = points[index];
            Vector3 p2 = points[Mathf.Clamp(index + 1, 0, points.Length - 1)];
            Vector3 p3 = points[Mathf.Clamp(index + 2, 0, points.Length - 1)];

            // Catmull-Rom spline formula
            return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);
        }
        
        void OnDrawGizmos()
        {
            if (_transformList == null || _transformList.Count < 2)
            {
                return;
            }

            if (_posList == null || _posList.Count < 1)
            {
                _posList = new List<Vector3>(_transformList.Count);
                foreach (var trans in _transformList)
                {
                    _posList.Add(trans.position);
                }
            }

            UpdatePath();
            var path = GetPath();
            if (path == null || path.Length < 2)
            {
                return;
            }

            Gizmos.color = _color;

            // Calculate the path using Catmull-Rom spline
            Vector3 previousPoint = path[0];
            int resolution = 10; // Number of segments between points
            for (int i = 1; i < path.Length; i++)
            {
                for (int j = 0; j <= resolution; j++)
                {
                    float t = j / (float)resolution;
                    Vector3 point = CatmullRom(path, i - 1, t);
                    Gizmos.DrawLine(previousPoint, point);
                    previousPoint = point;
                }
            }
        }
#endif
    }
