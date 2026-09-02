-- 004_seed_dict.sql
-- 工业术语同义词词典种子数据
-- 幂等：ON CONFLICT (canonical, scope) DO UPDATE

INSERT INTO kb.term_dict (canonical, synonyms, scope) VALUES
  -- 主轴相关
  ('主轴',  ARRAY['spindle','主轴头','刀轴','SP','sp'],                    'cnc'),
  ('伺服',  ARRAY['servo','servo amplifier','伺服放大器','伺服电机','SV'],  'cnc'),
  ('主轴伺服', ARRAY['spindle servo','主轴伺服','spindle amplifier'],      'cnc'),
  ('主轴电机', ARRAY['spindle motor','主轴马达'],                          'cnc'),

  -- 进给轴
  ('进给',  ARRAY['feed','进给轴','feed axis','FA'],                        'cnc'),
  ('X轴',   ARRAY['X axis','X轴伺服'],                                     'cnc'),
  ('Y轴',   ARRAY['Y axis','Y轴伺服'],                                     'cnc'),
  ('Z轴',   ARRAY['Z axis','Z轴伺服','Z轴方向'],                           'cnc'),

  -- 报警与状态
  ('超程',  ARRAY['overtravel','over travel','软限位','硬限位','OT'],      'cnc'),
  ('急停',  ARRAY['emergency stop','E-stop','急停按钮','紧急停止','EMG'],  'cnc'),
  ('复位',  ARRAY['reset','重置','清除报警','clear'],                      'cnc'),
  ('报警',  ARRAY['alarm','AL','警报','故障报警'],                        'cnc'),
  ('故障',  ARRAY['fault','failure','error','异常'],                       'cnc'),

  -- 设备形态
  ('加工中心', ARRAY['machining center','MC','立加','加工机床'],           'cnc'),
  ('车床',    ARRAY['lathe','CNC车床','车削中心'],                         'cnc'),

  -- 信号/接口
  ('VRDY',     ARRAY['velocity ready','速度就绪','VRDY信号'],              'cnc'),
  ('PRDY',     ARRAY['position ready','位置就绪','PRDY信号'],              'cnc'),
  ('PMC',      ARRAY['programmable machine controller','PMC控制','PMC梯形图'], 'cnc'),

  -- 工具/工装
  ('刀具',  ARRAY['tool','cutter','刀片','刀头'],                          'cnc'),
  ('换刀',  ARRAY['tool change','ATC','自动换刀','刀具交换'],              'cnc'),
  ('刀库',  ARRAY['magazine','tool magazine','ATC刀库'],                   'cnc'),

  -- 液压/气动
  ('液压',  ARRAY['hydraulic','液压系统','油压'],                          'cnc'),
  ('气动',  ARRAY['pneumatic','气动系统','气压'],                         'cnc'),

  -- 控制系统
  ('FANUC', ARRAY['fanuc','发那科','FANUC系统'],                           'cnc'),
  ('三菱',  ARRAY['mitsubishi','meldas','三菱系统'],                       'cnc'),
  ('西门子', ARRAY['siemens','sinumerik','西门子系统','828D','840D'],      'cnc')

ON CONFLICT (canonical, scope) DO UPDATE
   SET synonyms = EXCLUDED.synonyms;

-- 一句话注释
COMMENT ON TABLE kb.term_dict IS '工业术语同义词词典（种子数据见 004_seed_dict.sql，可继续追加）';
