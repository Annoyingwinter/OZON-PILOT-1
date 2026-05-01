using System;
using System.Collections.Generic;

namespace LitchiOzonRecovery
{
    internal sealed class OzonProductImageAdaptationModule
    {
        private readonly RussianImageAdaptationModule _imageModule;
        private readonly IProductImagePublisher _publisher;
        private readonly RussianImageAdaptationOptions _imageOptions;

        public OzonProductImageAdaptationModule()
            : this(
                new RussianImageAdaptationModule(),
                new HttpProductImagePublisher(HttpProductImagePublisherOptions.FromEnvironment()),
                RussianImageAdaptationOptions.FromEnvironment())
        {
        }

        public OzonProductImageAdaptationModule(
            RussianImageAdaptationModule imageModule,
            IProductImagePublisher publisher,
            RussianImageAdaptationOptions imageOptions)
        {
            _imageModule = imageModule;
            _publisher = publisher;
            _imageOptions = imageOptions;
        }

        public void AdaptPassedProductImages(IList<SourceProduct> products, Action<string> log)
        {
            if (products == null || products.Count == 0)
            {
                Write(log, "[image-adapter] no products to adapt.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_imageOptions.ApiKey))
            {
                Write(log, "[image-adapter] skipped: CODEXMANAGER_API_KEY or OPENAI_API_KEY is not configured.");
                return;
            }

            Write(log, "[image-adapter] start: super-resolution + Russian cultural adaptation + 3:4 output.");

            int adapted = 0;
            int skipped = 0;
            for (int i = 0; i < products.Count; i++)
            {
                SourceProduct product = products[i];
                if (product == null)
                {
                    skipped += 1;
                    continue;
                }

                if (AdaptPassedProductImage(product, log))
                {
                    adapted += 1;
                }
                else
                {
                    skipped += 1;
                }
            }

            Write(log, "[image-adapter] complete: adapted=" + adapted + ", skipped=" + skipped + ".");
        }

        public bool AdaptPassedProductImage(SourceProduct product, Action<string> log)
        {
            if (product == null)
            {
                Write(log, "[image-adapter] skip: product is empty.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(_imageOptions.ApiKey))
            {
                Write(log, "[image-adapter] skipped: CODEXMANAGER_API_KEY or OPENAI_API_KEY is not configured.");
                return false;
            }

            if (!IsPassedProduct(product))
            {
                Write(log, "[image-adapter] skip " + SafeOfferId(product) + ": decision=" + SafeText(product.Decision));
                return false;
            }

            string sourceImage = ResolveSourceImage(product);
            if (string.IsNullOrEmpty(sourceImage))
            {
                Write(log, "[image-adapter] skip " + SafeOfferId(product) + ": no source image.");
                return false;
            }

            try
            {
                Write(log, "[image-adapter] processing " + SafeOfferId(product) + ": super-resolution, Russian culture, 3:4.");
                RussianImageAdaptationResult image = _imageModule.AdaptProductImage(
                    new RussianImageAdaptationRequest
                    {
                        ProductTitle = product.Title,
                        RussianTitle = product.RussianTitle,
                        Category = product.Keyword,
                        SourceImageUrl = sourceImage,
                        OutputFileNamePrefix = SafeOfferId(product)
                    },
                    _imageOptions);

                if (!image.Success)
                {
                    Write(log, "[image-adapter] failed " + SafeOfferId(product) + ": " + SafeText(image.ErrorMessage));
                    return false;
                }

                string publicUrl = image.ImageUrl;
                if (!string.IsNullOrEmpty(image.ImagePath))
                {
                    Write(log, "[image-adapter] publishing " + SafeOfferId(product) + ": " + image.ImagePath);
                    publicUrl = _publisher.Publish(image.ImagePath);
                }

                if (string.IsNullOrEmpty(publicUrl))
                {
                    Write(log, "[image-adapter] failed " + SafeOfferId(product) + ": no public image URL.");
                    return false;
                }

                ReplaceProductImage(product, publicUrl);
                Write(log, "[image-adapter] done " + SafeOfferId(product) + ": " + publicUrl);
                return true;
            }
            catch (Exception ex)
            {
                Write(log, "[image-adapter] error " + SafeOfferId(product) + ": " + ex.Message);
                return false;
            }
        }

        private static bool IsPassedProduct(SourceProduct product)
        {
            return string.Equals(product.Decision, "Go", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveSourceImage(SourceProduct product)
        {
            if (!string.IsNullOrEmpty(product.MainImage))
            {
                return product.MainImage;
            }

            return product.Images != null && product.Images.Count > 0 ? product.Images[0] : string.Empty;
        }

        private static void ReplaceProductImage(SourceProduct product, string publicUrl)
        {
            product.MainImage = publicUrl;
            if (product.Images == null)
            {
                product.Images = new List<string>();
            }

            if (!product.Images.Contains(publicUrl))
            {
                product.Images.Insert(0, publicUrl);
            }
        }

        private static string SafeOfferId(SourceProduct product)
        {
            return product == null || string.IsNullOrEmpty(product.OfferId) ? "(no-offer-id)" : product.OfferId;
        }

        private static string SafeText(string value)
        {
            return string.IsNullOrEmpty(value) ? "(none)" : value;
        }

        private static void Write(Action<string> log, string message)
        {
            if (log != null)
            {
                log(message);
            }
        }
    }
}
