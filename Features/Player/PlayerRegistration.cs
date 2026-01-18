using Microsoft.Extensions.DependencyInjection;
using Wideor.App.Shared.Infra;

namespace Wideor.App.Features.Player
{
    /// <summary>
    /// Player機能スライスのDI登録用拡張メソッド
    /// </summary>
    public static class PlayerRegistration
    {
        /// <summary>
        /// Player機能スライスをDIコンテナに登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <returns>サービスコレクション（チェーン可能）</returns>
        public static IServiceCollection AddPlayerFeature(this IServiceCollection services)
        {
            // PlayerViewModelをスコープ付きで登録
            // 各Viewごとに新しいインスタンスが必要な場合はTransientに変更
            services.AddScoped<PlayerViewModel>();

            // PlayerViewは通常、XAMLから直接インスタンス化されるため、
            // DI登録は不要（必要に応じてAddTransientで登録可能）

            return services;
        }

        /// <summary>
        /// Player機能スライスをDIコンテナに登録します（ViewModelをTransientで登録）。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <returns>サービスコレクション（チェーン可能）</returns>
        public static IServiceCollection AddPlayerFeatureTransient(this IServiceCollection services)
        {
            services.AddTransient<PlayerViewModel>();
            return services;
        }
    }
}
