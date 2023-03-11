﻿using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using PluginCore.IPlugins;
using MemosPlus.Utils;
using System.Text;
using Octokit;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using PluginCore;
using Scriban;

namespace MemosPlus
{
    public class MemosPlus : BasePlugin, IWidgetPlugin, IStartupXPlugin, ITimeJobPlugin
    {

        #region Props
        public long SecondsPeriod
        {
            get
            {
                var settings = PluginSettingsModelFactory.Create<SettingsModel>(nameof(MemosPlus));

                return settings.SecondsPeriod;
            }
        }
        #endregion

        public override (bool IsSuccess, string Message) AfterEnable()
        {
            Console.WriteLine($"{nameof(MemosPlus)}: {nameof(AfterEnable)}");
            return base.AfterEnable();
        }

        public override (bool IsSuccess, string Message) BeforeDisable()
        {
            Console.WriteLine($"{nameof(MemosPlus)}: {nameof(BeforeDisable)}");
            return base.BeforeDisable();
        }

        #region 定时任务
        public async Task ExecuteAsync()
        {
            try
            {
                var settings = PluginSettingsModelFactory.Create<SettingsModel>(nameof(MemosPlus));

                #region 备份到 GitHub
                // TODO: 备份到 GitHub
                if (settings.Backup.EnableBackupToGitHub)
                {
                    MemosUtil memosUtil = new MemosUtil(settings.Memos.BaseUrl);
                    int offset = 0;
                    var list = memosUtil.List(memosSession: settings.Memos.MemosSession, offset: 0, limit: 20);
                    GitHubUtil gitHubUtil = new GitHubUtil();
                    settings.GitHub.RepoTargetDirPath = settings.GitHub.RepoTargetDirPath.Trim().TrimEnd('/');

                    // 解析模版
                    string githubTemplateFilePath = Path.Combine(PluginPathProvider.PluginsRootPath(), nameof(MemosPlus), "templates", "github.md");
                    string githubTemplateContent = File.ReadAllText(githubTemplateFilePath, System.Text.Encoding.UTF8);
                    // https://github.com/scriban/scriban
                    var githubTemplate = Template.Parse(githubTemplateContent);
                    List<string> memoFilePaths = new List<string>();
                    while (list != null && list.Count >= 1)
                    {
                        foreach (Utils.MemoItemModel item in list)
                        {
                            try
                            {
                                // 纯文本
                                string createTimeStr = item.createdTs.ToDateTime10().ToString("yyyy-MM-dd HH:mm:ss");
                                string updateTimeStr = item.updatedTs.ToDateTime10().ToString("yyyy-MM-dd HH:mm:ss");
                                // TODO: 这里发现 scriban 有个 Bug: CreateTime, UpdateTime 这两个属性名解析为空, 还不会报错
                                string githubRenderResult = githubTemplate.Render(new
                                {
                                    Memo = item,
                                    Date = createTimeStr,
                                    Updated = updateTimeStr,
                                    Public = item.visibility != "PRIVATE"
                                });
                                string repoTargetFilePath = $"{settings.GitHub.RepoTargetDirPath}/{item.creatorName}/{item.createdTs.ToDateTime10().ToString("yyyy-MM-dd HH-mm-ss")}.md";
                                gitHubUtil.UpdateFile(
                                    repoOwner: settings.GitHub.RepoOwner,
                                    repoName: settings.GitHub.RepoName,
                                    repoBranch: settings.GitHub.RepoBranch,
                                    repoTargetFilePath: repoTargetFilePath,
                                    fileContent: githubRenderResult,
                                    accessToken: settings.GitHub.AccessToken
                                );
                                memoFilePaths.Add(repoTargetFilePath);

                                // TODO: 资源文件
                                // 注意: 资源文件路径也要放入其中
                                // memoFilePaths.Add();

                            }
                            catch (System.Exception ex)
                            {
                                System.Console.WriteLine(ex.ToString());
                            }
                        }
                        offset = offset + list.Count;
                        list = memosUtil.List(memosSession: settings.Memos.MemosSession, offset: offset, limit: 20);
                    }
                    // 清理不存在的文件: 对于存放 memos 的文件夹, 清理 memos 中已删除的对应文件
                    List<string> githubExistFilePaths = gitHubUtil.Files(
                        repoOwner: settings.GitHub.RepoOwner,
                        repoName: settings.GitHub.RepoName,
                        repoBranch: settings.GitHub.RepoBranch,
                        repoTargetDirPath: $"{settings.GitHub.RepoTargetDirPath}/",
                        accessToken: settings.GitHub.AccessToken);
                    // 对于 GitHub 存在 但 memos 并没有此对应文件, 就需要删除
                    List<string> deletedFilePaths = githubExistFilePaths.Where(m => !memoFilePaths.Contains(m)).ToList();
                    foreach (var deletedFilePath in deletedFilePaths)
                    {
                        try
                        {
                            gitHubUtil.Delete(
                                repoOwner: settings.GitHub.RepoOwner,
                                repoName: settings.GitHub.RepoName,
                                repoBranch: settings.GitHub.RepoBranch,
                                repoTargetFilePath: deletedFilePath,
                                accessToken: settings.GitHub.AccessToken
                            );
                        }
                        catch (System.Exception ex)
                        {
                            System.Console.WriteLine($"删除失败: {deletedFilePath}");
                            System.Console.WriteLine(ex.ToString());
                        }
                    }
                }
                #endregion

            }
            catch (Exception ex)
            {
                Console.WriteLine($"执行定时任务失败: {ex.ToString()}");
            }

            await Task.CompletedTask;
        }
        #endregion

        public async Task<string> Widget(string widgetKey, params string[] extraPars)
        {
            string rtnStr = null;
            if (widgetKey == "memos")
            {
                if (extraPars != null && extraPars.Length >= 1)
                {
                    Console.WriteLine(string.Join(",", extraPars));
                    string memosVersion = extraPars[0];
                    string memosPart = "";
                    if (extraPars.Length >= 2)
                    {
                        memosPart = extraPars[1];
                        switch (memosPart)
                        {
                            case "banner-wrapper":
                                // banner-wrapper
                                rtnStr = @"<script>
                                    console.log(""测试"");
                                    </script>";
                                break;
                            default:
                                break;
                        }

                    }

                }
            }

            return await Task.FromResult(rtnStr);
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseMiddleware<Middlewares.CorsMiddleware>();
        }

        public void ConfigureServices(IServiceCollection services)
        {

        }

        public int ConfigureServicesOrder => 0;

        public int ConfigureOrder => 0;

    }
}
