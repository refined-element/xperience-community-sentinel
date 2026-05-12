// Kentico's webpack config is `module.exports = (opts) => ({...})` — a single factory, not a
// named export. Require returns the factory directly; we invoke it and extend the result with
// our TypeScript loader rules because the base config only sets up entry/output/externals
// (notably, it does NOT bundle React — it's an external the admin shell provides at runtime).
//
// orgName + projectName MUST match:
//   - <AdminOrgName> in XperienceCommunity.Sentinel.Admin.csproj
//   - <AdminClientPath ... ProjectName="..."> in the same csproj
//   - SentinelAdminModule.OnInit's RegisterClientModule(orgName, projectName) call
//   - The templateName prefix used in [UIPage(..., templateName: "@xperiencecommunity/sentinel-admin/...")]
//
// Any drift between the four locations = blank admin page at runtime with no helpful error.
const kenticoWebpackConfig = require('@kentico/xperience-webpack-config');

module.exports = (webpackConfigEnv, argv) => {
    const baseConfig = kenticoWebpackConfig({
        orgName: 'xperiencecommunity',
        projectName: 'sentinel-admin',
        webpackConfigEnv,
        argv,
    });

    return {
        ...baseConfig,
        module: {
            ...(baseConfig.module || {}),
            rules: [
                ...((baseConfig.module && baseConfig.module.rules) || []),
                {
                    // ts-loader handles both .ts and .tsx. transpileOnly skips full type checking
                    // during bundle — tsc in editor + CI still catches errors; loader stays fast.
                    test: /\.tsx?$/,
                    use: {
                        loader: 'ts-loader',
                        options: {
                            transpileOnly: true,
                        },
                    },
                    exclude: /node_modules/,
                },
            ],
        },
    };
};
