using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using UnityEngine.Events;

    /// <summary>
    /// UI をスライドで出し入れする。初期位置(基準)を保持し、毎回そこへ帰すことで
    /// 連打・多重呼び出しでもドリフトしないようにする。
    /// </summary>
    public class AppearingOnSlideEasing : MonoBehaviour
    {
        [Header("Motion")]
        [SerializeField] private Vector3 _offsetFromBase = new Vector3(0f, 1000f, 0f); // 旧:_defaultPos
        [SerializeField] private float _duration = 0.5f;
        [SerializeField] private Ease _easeType = Ease.OutCubic;
        [SerializeField] private bool _ignoreTimeScale = true;
        [SerializeField] private Ease _inverseEaseType = Ease.InCubic;

        [Header("Events")]
        [SerializeField] private UnityEvent _onEaseComplete;

        [Header("Safety")]
        [Tooltip("開閉の最小間隔（秒）。0で無効")]
        [SerializeField] private float _minToggleInterval = 0.05f;

        private RectTransform _rect;
        private Vector3 _basePos;          // 基準位置（必ずここに戻す）
        private bool _useRect;             // RectTransform を使うか
        private Tween _tween;              // 現在の Tween を保持して毎回 Kill
        private Button[] _buttons;
        private Button _button;
        private bool _isAnimating = false;
        private float _lastToggleTime = -999f;

        private void Awake()
        {
            _rect = GetComponent<RectTransform>();
            _useRect = _rect != null;
            CacheBasePosition(); // Awake 時点の位置を基準にする
        }

        private void CacheBasePosition()
        {
            if (_useRect)
                _basePos = _rect.anchoredPosition3D;
            else
                _basePos = transform.localPosition;
        }

        private void OnEnable()
        {
            EnsureButtonsCache();
            SetInteractableAll(false);

            KillActiveTween(complete: false);
            // 「登場」開始前にオフセット位置へ即座にスナップ
            if (_useRect)
            {
                _rect.anchoredPosition3D = _basePos + _offsetFromBase;
                _tween = _rect.DOAnchorPos3D(_basePos, _duration)
                              .SetEase(_easeType)
                              .SetUpdate(_ignoreTimeScale)
                              .OnComplete(OnOpenComplete);
            }
            else
            {
                transform.localPosition = _basePos + _offsetFromBase;
                _tween = transform.DOLocalMove(_basePos, _duration)
                                  .SetEase(_easeType)
                                  .SetUpdate(_ignoreTimeScale)
                                  .OnComplete(OnOpenComplete);
            }
            _isAnimating = true;
            _lastToggleTime = Time.unscaledTime;
        }

        private void OnDisable()
        {
            EnsureButtonsCache();
            SetInteractableAll(false);
            // 念のため停止（OnDisable連打でも漏れないように）
            KillActiveTween(complete: false);

            // ドリフト防止の最終保険：常に基準へ戻す
            SnapToBase();
            _isAnimating = false;
        }

        private void EnsureButtonsCache()
        {
            if (_buttons == null) _buttons = GetComponentsInChildren<Button>(includeInactive: true);
            if (_button == null) _button = GetComponent<Button>();
        }

        private void SetInteractableAll(bool v)
        {
            if (_buttons != null)
            {
                foreach (var b in _buttons) if (b) b.interactable = v;
            }
            if (_button) _button.enabled = v;
        }

        private void OnOpenComplete()
        {
            _isAnimating = false;
            SetInteractableAll(true);
            _onEaseComplete?.Invoke();
        }

        /// <summary>
        /// 閉じる（スライドアウト）。完了時に非表示にし、基準へスナップする。
        /// 連打や多重呼び出しでもドリフトしない。
        /// </summary>
        public void Invert()
        {
            if (_minToggleInterval > 0f &&
                (Time.unscaledTime - _lastToggleTime) < _minToggleInterval)
            {
                // クールタイム中は無視（必要なければ _minToggleInterval=0 に）
                return;
            }
            _lastToggleTime = Time.unscaledTime;

            EnsureButtonsCache();
            SetInteractableAll(false);

            // すでに開閉中なら止める
            if (_isAnimating && _tween != null && _tween.IsActive())
            {
                // 強制的に現在のTweenを止める（完了させず Kill）
                _tween.Kill(complete: false);
            }

            _isAnimating = true;

            if (_useRect)
            {
                // 基準 → 基準+オフセットへ
                KillActiveTween(complete: false);
                _tween = _rect.DOAnchorPos3D(_basePos + _offsetFromBase, _duration)
                              .SetEase(_inverseEaseType)
                              .SetUpdate(_ignoreTimeScale)
                              .OnComplete(OnCloseComplete);
            }
            else
            {
                KillActiveTween(complete: false);
                _tween = transform.DOLocalMove(_basePos + _offsetFromBase, _duration)
                                  .SetEase(_inverseEaseType)
                                  .SetUpdate(_ignoreTimeScale)
                                  .OnComplete(OnCloseComplete);
            }
        }

        private void OnCloseComplete()
        {
            // 完了時に確実に基準へ戻して非表示
            SnapToBase();
            gameObject.SetActive(false);
            _isAnimating = false;
        }

        /// <summary>
        /// 現在の Tween を停止
        /// </summary>
        private void KillActiveTween(bool complete)
        {
            if (_tween != null && _tween.IsActive())
            {
                if (complete) _tween.Complete(true);
                _tween.Kill(false);
                _tween = null;
            }
        }

        /// <summary>
        /// 位置を基準へ強制スナップ（保険）
        /// </summary>
        public void SnapToBase()
        {
            if (_useRect)
                _rect.anchoredPosition3D = _basePos;
            else
                transform.localPosition = _basePos;
        }

        /// <summary>
        /// 実行時に基準位置を再キャプチャしたい場合に呼べる（任意）
        /// </summary>
        public void RecalibrateBaseFromCurrent()
        {
            if (_useRect)
                _basePos = _rect.anchoredPosition3D;
            else
                _basePos = transform.localPosition;
        }
    }
