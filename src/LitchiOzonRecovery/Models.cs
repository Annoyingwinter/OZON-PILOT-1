using System.Collections.Generic;
using System.ComponentModel;

namespace LitchiOzonRecovery
{
    public sealed class AppConfig
    {
        [Category("基础设置"), DisplayName("保存目录")]
        public string SaveUrl { get; set; }

        [Category("价格筛选"), DisplayName("最低售价")]
        public decimal MinPirce { get; set; }

        [Category("价格筛选"), DisplayName("最高售价")]
        public decimal MaxPrice { get; set; }

        [Category("销量筛选"), DisplayName("最低销量")]
        public int MinSaleNum { get; set; }

        [Category("销量筛选"), DisplayName("最高销量")]
        public int MaxSaleNum { get; set; }

        [Category("重量筛选"), DisplayName("最低重量(g)")]
        public int MinWeight { get; set; }

        [Category("重量筛选"), DisplayName("最高重量(g)")]
        public int MaxWeight { get; set; }

        [Category("供货筛选"), DisplayName("最大供货数量")]
        public int MaxGmNum { get; set; }

        [Category("利润设置"), DisplayName("最低利润率(%)")]
        public decimal MinProfitPer { get; set; }

        [Category("利润设置"), DisplayName("默认运费")]
        public decimal DeliveryFee { get; set; }

        [Category("供货筛选"), DisplayName("过滤供货价")]
        public bool IsFilterGmPrice { get; set; }

        [Category("店铺筛选"), DisplayName("扩展店铺")]
        public bool IsExtendShop { get; set; }

        [Category("插件设置"), DisplayName("启用 1688 模式")]
        public bool Is1688 { get; set; }

        [Category("利润设置"), DisplayName("自动利润")]
        public bool IsAutoProfit { get; set; }

        [Category("品牌筛选"), DisplayName("过滤品牌")]
        public bool IsFilterBrand { get; set; }

        [Category("店铺筛选"), DisplayName("过滤低评分")]
        public bool IsFilterRate4 { get; set; }

        [Category("供货筛选"), DisplayName("最低供货价")]
        public decimal MinGmPirce { get; set; }

        [Category("供货筛选"), DisplayName("启用最低供货销量")]
        public bool IsMinGmSale { get; set; }

        [Category("店铺筛选"), DisplayName("优质店铺优先")]
        public bool IsBetterShop { get; set; }

        [Category("类目筛选"), DisplayName("过滤类目 ID")]
        public List<long> FilterCategoryIds { get; set; }

        [Category("时间筛选"), DisplayName("SKU 上架天数")]
        public int SkuUpDays { get; set; }

        [Category("供货筛选"), DisplayName("使用供货 B 价")]
        public bool IsGmBPrice { get; set; }

        [Category("SKU 筛选"), DisplayName("过滤 SKU")]
        public bool IsFilterSku { get; set; }

        [Category("店铺筛选"), DisplayName("过滤店铺")]
        public bool IsFilterShop { get; set; }

        [Category("其他筛选"), DisplayName("过滤异常项")]
        public bool IsFilterCZ { get; set; }

        [Category("价格筛选"), DisplayName("启用价格校验")]
        public bool IsCheckPrice { get; set; }

        [Category("利润设置"), DisplayName("智能利润比例(%)")]
        public decimal ZNPer { get; set; }

        [Category("履约模式"), DisplayName("按 FBO 计算")]
        public bool IsFbo { get; set; }

        [Category("导出设置"), DisplayName("自动导出")]
        public bool IsAutoExport { get; set; }

        [Category("抓取设置"), DisplayName("抓取数量")]
        public int CatchNum { get; set; }

        [Category("店铺筛选"), DisplayName("黑名单店铺")]
        public List<string> BlackShops { get; set; }

        [Category("店铺筛选"), DisplayName("店铺成交数")]
        public int ShopTradeNum { get; set; }

        [Category("店铺筛选"), DisplayName("店铺最少在售 SKU")]
        public int ShopSaleSkuMin { get; set; }

        [Category("店铺筛选"), DisplayName("店铺最多在售 SKU")]
        public int ShopSaleSkuMax { get; set; }

        [Category("云筛选"), DisplayName("启用云筛选")]
        public bool IsCloudFilter { get; set; }

        [Category("云筛选"), DisplayName("云筛选代码")]
        public string CloudFilterCode { get; set; }

        [Category("更新设置"), DisplayName("自动更新配置")]
        public string AutoUp { get; set; }

        public static AppConfig CreateDefault()
        {
            return new AppConfig
            {
                SaveUrl = @"D:\auto-update",
                MinPirce = 350m,
                MaxPrice = 99999m,
                MinSaleNum = 2,
                MaxSaleNum = 9999,
                MinWeight = 10,
                MaxWeight = 50000,
                MaxGmNum = 50,
                MinProfitPer = 20m,
                DeliveryFee = 3m,
                IsAutoProfit = true,
                IsExtendShop = true,
                IsFilterRate4 = true,
                IsFilterSku = true,
                ShopTradeNum = 100,
                ShopSaleSkuMin = 100,
                ShopSaleSkuMax = 1000,
                ZNPer = 40m,
                BlackShops = new List<string>(),
                FilterCategoryIds = new List<long>()
            };
        }
    }

    public sealed class FeeRule
    {
        [DisplayName("序号")]
        public int Id { get; set; }

        [DisplayName("一级类目 ID")]
        public long CategoryId1 { get; set; }

        [DisplayName("二级类目 ID")]
        public long CategoryId2 { get; set; }

        [DisplayName("一级类目")]
        public string Category1 { get; set; }

        [DisplayName("二级类目")]
        public string Category2 { get; set; }

        [DisplayName("FBS <=500g")]
        public decimal FBS { get; set; }

        [DisplayName("FBS <=1500g")]
        public decimal FBS1500 { get; set; }

        [DisplayName("FBS >1500g")]
        public decimal FBS5000 { get; set; }

        [DisplayName("FBP <=500g")]
        public decimal FBP { get; set; }

        [DisplayName("FBP <=1500g")]
        public decimal FBP1500 { get; set; }

        [DisplayName("FBP >1500g")]
        public decimal FBP5000 { get; set; }

        [DisplayName("FBO <=500g")]
        public decimal FBO { get; set; }

        [DisplayName("FBO <=1500g")]
        public decimal FBO1500 { get; set; }

        [DisplayName("FBO >1500g")]
        public decimal FBO5000 { get; set; }

        public override string ToString()
        {
            return Category1 + " / " + Category2 + " (" + CategoryId1 + "/" + CategoryId2 + ")";
        }
    }

    public sealed class CategoryNode
    {
        public string DescriptionCategoryId { get; set; }
        public string DescriptionCategoryName { get; set; }
        public string DescriptionTypeId { get; set; }
        public string DescriptionTypeName { get; set; }
        public bool Disabled { get; set; }
        public List<CategoryNode> Children { get; private set; }

        public CategoryNode()
        {
            Children = new List<CategoryNode>();
        }

        public override string ToString()
        {
            if (string.IsNullOrEmpty(DescriptionCategoryName))
            {
                return DescriptionCategoryId;
            }

            return DescriptionCategoryName + " [" + DescriptionCategoryId + "]";
        }
    }

    public sealed class AssetSnapshot
    {
        public AppConfig Config { get; set; }
        public List<CategoryNode> Categories { get; set; }
        public List<FeeRule> FeeRules { get; set; }
        public Dictionary<string, long> TableCounts { get; set; }
    }

    public sealed class ProfitInput
    {
        public long CategoryId1 { get; set; }
        public long CategoryId2 { get; set; }
        public decimal SourcePrice { get; set; }
        public decimal WeightGrams { get; set; }
        public decimal DeliveryFee { get; set; }
        public decimal OtherCost { get; set; }
        public decimal TargetProfitPercent { get; set; }
        public decimal ManualSellingPrice { get; set; }
        public string FulfillmentMode { get; set; }
    }

    public sealed class ProfitEstimate
    {
        public FeeRule MatchedRule { get; set; }
        public decimal LogisticsFee { get; set; }
        public decimal EstimatedCost { get; set; }
        public decimal SuggestedSellingPrice { get; set; }
        public decimal ActualSellingPrice { get; set; }
        public decimal ProfitAmount { get; set; }
        public decimal ProfitPercent { get; set; }
        public bool MeetsPriceFilter { get; set; }
        public bool MeetsWeightFilter { get; set; }
        public bool MeetsProfitFilter { get; set; }
        public string Notes { get; set; }
    }
}
