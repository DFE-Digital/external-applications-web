const { defineConfig } = require("cypress");

module.exports = defineConfig({
  e2e: {
    setupNodeEvents(on, config) {
      // Accept self-signed certificates
      on('before:browser:launch', (browser, launchOptions) => {
        if (browser.name === 'chrome') {
          launchOptions.args.push('--ignore-certificate-errors')
          launchOptions.args.push('--allow-insecure-localhost')
        }
        return launchOptions
      })
      
      return config
    }
  },
});
