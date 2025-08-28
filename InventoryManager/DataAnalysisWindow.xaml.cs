using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace InventoryManager
{
    public partial class DataAnalysisWindow : Window
    {
        private MainWindow _mainWindow; // Referensi ke jendela utama

        public DataAnalysisWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            Loaded += (s, e) => RefreshData(); // Muat data saat pertama kali dibuka
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshData();
        }

        private void RefreshData()
        {
            // Minta data terbaru dari MainWindow
            var allProducts = _mainWindow.GetAllProductsForAnalysis();
            var allConversions = _mainWindow.GetAllConversionsForAnalysis();

            // Jalankan ulang semua analisis
            AnalyzeProductsWithNoUnits(allProducts, allConversions);
            AnalyzeUnitsWithNoBarcode(allProducts, allConversions);
            AnalyzeDuplicateBarcodes(allProducts, allConversions);
            AnalyzeProductsWithNoPrice(allProducts, allConversions);
        }

        private void AnalyzeProductsWithNoUnits(List<Product> products, List<ProductUnitConversion> conversions)
        {
            var productsWithNoUnits = products
                .Where(p => !conversions.Any(c => c.ProductID == p.ProductId))
                .ToList();

            NoUnitsListView.ItemsSource = productsWithNoUnits;
        }

        private void AnalyzeUnitsWithNoBarcode(List<Product> products, List<ProductUnitConversion> conversions)
        {
            var unitsWithNoBarcode = conversions
                .Where(c => string.IsNullOrWhiteSpace(c.Barcode))
                .Select(c => new
                {
                    ProductName = products.FirstOrDefault(p => p.ProductId == c.ProductID)?.ProductName,
                    c.UnitName
                })
                .ToList();

            NoBarcodeListView.ItemsSource = unitsWithNoBarcode;
        }

        private void AnalyzeDuplicateBarcodes(List<Product> products, List<ProductUnitConversion> conversions)
        {
            var duplicateBarcodes = conversions
                .Where(c => !string.IsNullOrWhiteSpace(c.Barcode))
                .GroupBy(c => c.Barcode)
                .Where(g => g.Count() > 1)
                .SelectMany(g => g)
                .Select(c => new
                {
                    ProductName = products.FirstOrDefault(p => p.ProductId == c.ProductID)?.ProductName,
                    c.UnitName,
                    c.Barcode
                })
                .ToList();

            DuplicateBarcodeListView.ItemsSource = duplicateBarcodes;
        }

        private void AnalyzeProductsWithNoPrice(List<Product> products, List<ProductUnitConversion> conversions)
        {
            var productsWithNoPrice = conversions
                .Where(c => !c.IsStockOnly && c.SellingPrice < 100)
                .Select(c => new
                {
                    ProductName = products.FirstOrDefault(p => p.ProductId == c.ProductID)?.ProductName,
                    c.UnitName
                })
                .ToList();

            NoPriceListView.ItemsSource = productsWithNoPrice;
        }
    }
}
