using TMPro;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Local-only presenter for the shared network wallet.
    /// NetworkVariable değiştiğinde her client kendi TMP text'ini günceller.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu(
        "Multiplayer/Progression/UI/Shared Wallet Text Presenter")]
    public sealed class SharedWalletTextPresenter : MonoBehaviour
    {
        [Header("Reference")]
        [SerializeField] private TMP_Text balanceText;

        [Header("Formatting")]
        [SerializeField] private string prefix;
        [SerializeField] private string suffix;
        [SerializeField] private bool useThousandsSeparator = true;
        [SerializeField] private string walletNotReadyText = "0";

        private NetworkSharedWallet _wallet;

        private void Reset()
        {
            balanceText = GetComponent<TMP_Text>();
        }

        private void Awake()
        {
            if (balanceText == null)
            {
                balanceText = GetComponent<TMP_Text>();
            }
        }

        private void OnEnable()
        {
            RebindWallet();
        }

        private void Update()
        {
            // Wallet network objesi UI'dan sonra spawn olabilir.
            // Bağlandıktan sonra yalnızca tek referans karşılaştırması yapar.
            if (_wallet != NetworkSharedWallet.Instance)
            {
                RebindWallet();
            }
        }

        private void OnDisable()
        {
            UnbindWallet();
        }

        private void RebindWallet()
        {
            UnbindWallet();

            _wallet = NetworkSharedWallet.Instance;

            if (_wallet == null)
            {
                SetNotReadyText();
                return;
            }

            _wallet.BalanceChanged += HandleBalanceChanged;
            RefreshText(_wallet.Balance);
        }

        private void UnbindWallet()
        {
            if (_wallet != null)
            {
                _wallet.BalanceChanged -= HandleBalanceChanged;
                _wallet = null;
            }
        }

        private void HandleBalanceChanged(
            long previousBalance,
            long currentBalance)
        {
            RefreshText(currentBalance);
        }

        private void RefreshText(long balance)
        {
            if (balanceText == null)
            {
                return;
            }

            string amount = balance.ToString(
                useThousandsSeparator ? "N0" : "0");

            balanceText.text = prefix + amount + suffix;
        }

        private void SetNotReadyText()
        {
            if (balanceText != null)
            {
                balanceText.text =
                    prefix + walletNotReadyText + suffix;
            }
        }
    }
}