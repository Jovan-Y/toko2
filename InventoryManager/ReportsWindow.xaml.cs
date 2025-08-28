using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Globalization;

namespace InventoryManager
{
    public partial class ReportsWindow : Window
    {
        private string _connectionString;
        private List<Product> _allProducts;
        private ObservableCollection<SaleRecapItem> RecapList = new ObservableCollection<SaleRecapItem>();
        private ObservableCollection<CreditReportItem> CreditList = new ObservableCollection<CreditReportItem>();
        private ObservableCollection<PurchaseHistoryItem> PurchaseList = new ObservableCollection<PurchaseHistoryItem>();

        public ReportsWindow(string connectionString, List<Product> allProducts)
        {
            InitializeComponent();
            _connectionString = connectionString;
            _allProducts = allProducts;
            RecapDataGrid.ItemsSource = RecapList;
            CreditDataGrid.ItemsSource = CreditList;
            PurchaseHistoryDataGrid.ItemsSource = PurchaseList;

            PeriodComboBox.SelectedIndex = 0;
            RecapDatePicker.SelectedDate = DateTime.Today;
            LoadCreditReport();
            LoadPurchaseHistory();
        }

        private void PeriodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RecapDatePicker == null) return;
            LoadDailyRecap();
        }

        private void RecapDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PeriodComboBox == null) return;
            LoadDailyRecap();
        }

        private void LoadDailyRecap()
        {
            RecapList.Clear();
            if (RecapDatePicker.SelectedDate == null || PeriodComboBox.SelectedItem == null) return;

            DateTime selectedDate = RecapDatePicker.SelectedDate.Value;
            string dateFilter = "";
            switch ((PeriodComboBox.SelectedItem as ComboBoxItem)?.Content.ToString())
            {
                case "Bulanan":
                    dateFilter = "YEAR(s.SaleDate) = @Year AND MONTH(s.SaleDate) = @Month";
                    break;
                case "Tahunan":
                    dateFilter = "YEAR(s.SaleDate) = @Year";
                    break;
                default: // Harian
                    dateFilter = "CAST(s.SaleDate AS DATE) = @Date";
                    break;
            }

            string query = $@"SELECT s.SaleDate, u.FullName, s.TotalAmount, s.PaymentMethod, s.AmountPaid, s.CustomerName
                             FROM Sales s LEFT JOIN Users u ON s.UserID = u.UserID
                             WHERE {dateFilter} ORDER BY s.SaleDate DESC;";

            using (var con = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(query, con))
            {
                cmd.Parameters.AddWithValue("@Date", selectedDate.Date);
                cmd.Parameters.AddWithValue("@Month", selectedDate.Month);
                cmd.Parameters.AddWithValue("@Year", selectedDate.Year);
                try
                {
                    con.Open();
                    var reader = cmd.ExecuteReader();
                    decimal total = 0;
                    while (reader.Read())
                    {
                        var item = new SaleRecapItem
                        {
                            SaleDate = reader.GetDateTime(0),
                            CashierName = reader.IsDBNull(1) ? "N/A" : reader.GetString(1),
                            TotalAmount = reader.GetDecimal(2),
                            PaymentMethod = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            AmountPaid = reader.IsDBNull(4) ? 0 : reader.GetDecimal(4),
                            CustomerName = reader.IsDBNull(5) ? "" : reader.GetString(5)
                        };
                        RecapList.Add(item);
                        total += item.TotalAmount;
                    }
                    TotalRecapTextBlock.Text = $"Total Penjualan: {total.ToString("C", new CultureInfo("id-ID"))}";
                }
                catch (Exception ex) { MessageBox.Show("Gagal memuat rekap: " + ex.Message); }
            }
        }

        private void LoadCreditReport()
        {
            CreditList.Clear();
            string query = @"SELECT s.SaleDate, s.CustomerName, (s.TotalAmount - s.AmountPaid) AS CreditAmount, 
                                    DATEDIFF(day, s.SaleDate, GETDATE()) AS CreditDuration, u.FullName
                             FROM Sales s LEFT JOIN Users u ON s.UserID = u.UserID
                             WHERE s.PaymentMethod = 'Kredit' AND (s.TotalAmount - s.AmountPaid) > 0
                             ORDER BY s.SaleDate ASC;";
            using (var con = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(query, con))
            {
                try
                {
                    con.Open();
                    var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        CreditList.Add(new CreditReportItem
                        {
                            SaleDate = reader.GetDateTime(0),
                            CustomerName = reader.GetString(1),
                            CreditAmount = reader.GetDecimal(2),
                            CreditDuration = reader.GetInt32(3),
                            CashierName = reader.IsDBNull(4) ? "N/A" : reader.GetString(4)
                        });
                    }
                }
                catch (Exception ex) { MessageBox.Show("Gagal memuat laporan kredit: " + ex.Message); }
            }
        }

        private void LoadPurchaseHistory()
        {
            PurchaseList.Clear();
            string query = "SELECT PurchaseDate, SupplierName, TotalAmount FROM Purchases ORDER BY PurchaseDate DESC;";
            using (var con = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(query, con))
            {
                try
                {
                    con.Open();
                    var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        PurchaseList.Add(new PurchaseHistoryItem
                        {
                            PurchaseDate = reader.GetDateTime(0),
                            SupplierName = reader.IsDBNull(1) ? "N/A" : reader.GetString(1),
                            TotalAmount = reader.GetDecimal(2)
                        });
                    }
                }
                catch (Exception ex) { MessageBox.Show("Gagal memuat riwayat pembelian: " + ex.Message); }
            }
        }

        private void AddPurchaseButton_Click(object sender, RoutedEventArgs e)
        {
            // PERBAIKAN: Tambahkan string kosong untuk parameter 'lastSupplier'
            AddPurchaseWindow purchaseWindow = new AddPurchaseWindow(_connectionString, _allProducts, "");
            purchaseWindow.Owner = this;
            if (purchaseWindow.ShowDialog() == true)
            {
                LoadPurchaseHistory();
                LoadCreditReport();
                // Juga muat ulang produk di window utama jika window ini ditutup
                (this.Owner as MainWindow)?.LoadAllProducts();
            }
        }
    }

    public class SaleRecapItem
    {
        public DateTime SaleDate { get; set; }
        public string CashierName { get; set; } = "";
        public decimal TotalAmount { get; set; }
        public string PaymentMethod { get; set; } = "";
        public decimal AmountPaid { get; set; }
        public string CustomerName { get; set; } = "";
    }

    public class CreditReportItem
    {
        public DateTime SaleDate { get; set; }
        public string CustomerName { get; set; } = "";
        public decimal CreditAmount { get; set; }
        public int CreditDuration { get; set; }
        public string CashierName { get; set; } = "";
    }

    public class PurchaseHistoryItem
    {
        public DateTime PurchaseDate { get; set; }
        public string SupplierName { get; set; } = "";
        public decimal TotalAmount { get; set; }
    }
}
