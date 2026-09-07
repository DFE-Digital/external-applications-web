import { BasePage } from './BasePage';

export class DashboardPage extends BasePage {
  private static readonly selectors = {
    startNewApplicationButton: '#start-new-application-button',
  } as const;

  async startNewApplication(): Promise<void> {
    await this.page.locator(DashboardPage.selectors.startNewApplicationButton).click();
  }
}
