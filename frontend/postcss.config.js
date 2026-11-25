const path = require('path');
const postcssMixins = require('postcss-mixins');
const postcssSimpleVars = require('postcss-simple-vars');
const postcssColorFunction = require('postcss-color-function');
const postcssNested = require('postcss-nested');
const reload = require('require-nocache')(module);

const cssVarsFiles = [
  './src/Styles/Variables/dimensions',
  './src/Styles/Variables/fonts',
  './src/Styles/Variables/animations',
  './src/Styles/Variables/zIndexes'
].map(require.resolve);

const mixinsFiles = ['cover', 'linkOverlay', 'scroller', 'truncate']
  .map((name) => path.join(__dirname, 'src/Styles/Mixins', `${name}.css`));

module.exports = {
  plugins: [
    postcssMixins({ mixinsFiles }),
    postcssSimpleVars({
      variables: () => cssVarsFiles.reduce((acc, vars) => Object.assign(acc, reload(vars)), {})
    }),
    postcssColorFunction(),
    postcssNested()
  ]
};
