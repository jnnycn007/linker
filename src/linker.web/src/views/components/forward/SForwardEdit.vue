<template>
    <PlanList ref="planDom" :machineid="machineId" category="sforward" :handles="state.handles">
        <el-dialog v-model="state.show" @open="handleOnShowList" append-to=".app-wrap" :title="$t('sforward.title',[machineName])" top="2vh" width="98%">
            <div>
                <div class="t-c head">
                    <el-button type="success" size="small" @click="handleAdd" :loading="state.loading">{{$t('common.add')}}</el-button>
                    <el-button size="small" @click="handleRefresh">{{$t('common.refresh')}}</el-button>
                </div>
                <el-table :data="state.data" size="small" border height="500" @cell-dblclick="handleCellClick">
                    <el-table-column property="Name" :label="$t('sforward.name')" width="120">
                        <template #default="scope">
                            <template v-if="scope.row.NameEditing && scope.row.Started==false ">
                                <el-input v-trim autofocus size="small" v-model="scope.row.Name"
                                    @blur="handleEditBlur(scope.row, 'Name')"></el-input>
                            </template>
                            <template v-else>
                                <a href="javascript:;" class="a-line" @click="handleEdit(scope.row, 'Name')">{{ scope.row.Name || $t('common.unknow') }}</a>
                            </template>
                        </template>
                    </el-table-column>
                    <el-table-column property="NodeId" :label="$t('sforward.nodes')" width="80">
                        <template #default="scope">
                            <template v-if="scope.row.NodeIdEditing && scope.row.Started==false ">
                                <el-select v-model="scope.row.NodeId" size="small" @change="handleEditBlur(scope.row, 'NodeId')">
                                    <el-option :value="item.NodeId" :label="item.Name" v-for="(item,index) in state.nodes"></el-option>
                                </el-select>
                            </template>
                            <template v-else>
                                <a href="javascript:;" class="a-line" @click="handleEdit(scope.row, 'NodeId')">{{ state.nodesNames[scope.row.NodeId] || $t('common.unknow') }}</a>
                            </template>
                        </template>
                    </el-table-column>
                    <el-table-column property="Temp" :label="$t('sforward.portordomain')" width="130">
                        <template #default="scope">
                            <template v-if="scope.row.TempEditing && scope.row.Started==false">
                                <el-input v-trim autofocus size="small" v-model="scope.row.Temp"
                                    @blur="handleEditBlur(scope.row, 'Temp')"></el-input>
                            </template>
                            <template v-else>
                                <a href="javascript:;" class="a-line" @click="handleEdit(scope.row, 'Temp')">
                                    <template v-if="scope.row.Msg">
                                        <div class="error red" :title="scope.row.Msg">
                                            <span>{{ scope.row.Temp }}</span>
                                            <el-icon size="20"><WarnTriangleFilled /></el-icon>
                                        </div>
                                    </template>
                                    <template v-else>
                                        <template v-if="state.nodesJson[scope.row.NodeId1]">
                                            <span :class="{green:scope.row.Started}">
                                            <template v-if="/^\d+$/.test(scope.row.Temp)">{{ state.nodesJson[scope.row.NodeId1].Domain || state.nodesJson[scope.row.NodeId1].Host.split(':')[0] }}:{{ scope.row.Temp }}</template>
                                            <template v-else-if="scope.row.Temp.indexOf('.')>=0">{{ scope.row.Temp }}:{{state.nodesJson[scope.row.NodeId1].WebPort}}</template>
                                            <template v-else>{{ scope.row.Temp }}.{{ state.nodesJson[scope.row.NodeId1].Domain || state.nodesJson[scope.row.NodeId1].Host.split(':')[0] }}:{{state.nodesJson[scope.row.NodeId1].WebPort}}</template>
                                            </span>
                                        </template>
                                        <template v-else>
                                            <span>{{ scope.row.Temp }}</span>
                                        </template>
                                    </template>
                                </a>
                            </template>
                        </template>
                    </el-table-column>
                    <el-table-column property="LocalEP" :label="$t('sforward.local')" width="100">
                        <template #default="scope">
                            <template v-if="scope.row.LocalEPEditing && scope.row.Started==false">
                                <el-input v-trim autofocus size="small" v-model="scope.row.LocalEP"
                                    @blur="handleEditBlur(scope.row, 'LocalEP')"></el-input>
                            </template>
                            <template v-else>
                                <a href="javascript:;" class="a-line" @click="handleEdit(scope.row, 'LocalEP')">
                                    <template v-if="scope.row.LocalMsg">
                                        <div class="error red" :title="scope.row.LocalMsg">
                                            <span>{{ scope.row.LocalEP }}</span>
                                            <el-icon size="20"><WarnTriangleFilled /></el-icon>
                                        </div>
                                    </template>
                                    <template v-else>
                                        <span :class="{green:scope.row.Started}">{{ scope.row.LocalEP }}</span>
                                    </template>
                                </a>
                            </template>
                        </template>
                    </el-table-column>
                    <el-table-column property="Started" :label="$t('sforward.status')" width="60">
                        <template #default="scope">
                            <el-switch disabled v-model="scope.row.Started" inline-prompt
                                :active-text="$t('sforward.yes')" :inactive-text="$t('sforward.no')" @click="handleStartChange(scope.row)" />
                        </template>
                    </el-table-column>
                    
                    <el-table-column prop="Plan" :label="$t('sforward.plan')" width="200">
                        <template #default="scope">
                            <div class="plan">
                                <p><el-icon><Select /></el-icon><PlanShow handle="start"  :keyid="scope.row.Id"></PlanShow></p>
                                <p><el-icon><CloseBold /></el-icon><PlanShow handle="stop"  :keyid="scope.row.Id"></PlanShow></p>
                            </div>
                        </template>
                    </el-table-column>
                    <el-table-column :label="$t('common.oper')" width="54" fixed="right">
                        <template #default="scope">
                            <el-popconfirm 
                            :confirm-button-text="$t('common.confirm')" 
                            :cancel-button-text="$t('common.cancel')" 
                            :title="$t('common.delSure',[''])"
                                @confirm="handleDel(scope.row.Id)">
                                <template #reference>
                                    <el-button type="danger" size="small"><el-icon><Delete /></el-icon></el-button>
                                </template>
                            </el-popconfirm>
                        </template>
                    </el-table-column>
                </el-table>
            </div>
        </el-dialog>
    </PlanList>
</template>
<script>
import { onMounted, onUnmounted,  reactive, ref, watch } from 'vue';
import { getSForward, removeSForward, sforwardAddClient,sforwardTestLocal, sforwardStop, sforwardStart, sforwardSubscribe } from '@/apis/sforward'
import { ElMessage } from 'element-plus';
import {WarnTriangleFilled,Delete,Select,CloseBold} from '@element-plus/icons-vue'
import { injectGlobalData } from '@/provide';
import { useSforward } from './sforward';
import PlanList from '../plan/PlanList.vue';
import PlanShow from '../plan/PlanShow.vue';
import { useI18n } from 'vue-i18n';
export default {
    props: ['data','modelValue'],
    emits: ['update:modelValue'],
    components:{WarnTriangleFilled,Delete,Select,CloseBold,PlanList,PlanShow},
    setup(props, { emit }) {

        const {t} = useI18n();
        const planDom = ref(null);
        const globalData = injectGlobalData();
        const sforward = useSforward();
        const state = reactive({
            bufferSize:globalData.value.bufferSize,
            show: true,
            data: [],
            nodes:[],
            nodesNames:{},
            nodesJson:{},
            timer:0,
            timer1:0,
            timer2:0,
            editing:false,
            loading:false,
            handles:[
                {label:t('sforward.open'),value:'start'},
                {label:t('sforward.close'),value:'stop'},
            ]
        });
        watch(() => state.show, (val) => {
            if (!val) {
                setTimeout(() => {
                    emit('update:modelValue', val);
                }, 300);
            }
        });
        const _sforwardTestLocal = ()=>{
            clearTimeout(state.timer);
            sforwardTestLocal(sforward.value.machineid).then((res)=>{
                state.timer = setTimeout(_sforwardTestLocal,1000);
            }).catch(()=>{
                state.timer = setTimeout(_sforwardTestLocal,1000);
            });
        }
        const _getSForward = () => {
            clearTimeout(state.timer1);
            if(state.editing==false){
                getSForward(sforward.value.machineid).then((res) => {
                    res.forEach(c=>{
                        c.Temp = (c.Domain || c.RemotePort).toString();
                        c.RemotePort = 0;
                        c.Domain = '';
                        c.NodeId1 = c.NodeId1 || c.NodeId;
                    });
                    state.data = res;
                    state.timer1 = setTimeout(_getSForward,1000);
                }).catch(() => {
                    state.timer1 = setTimeout(_getSForward,1000);
                });
            }else{
                state.timer1 = setTimeout(_getSForward,1000);
            }
        }


        const handleOnShowList = () => {
            _getSForward();
        }

        const handleCellClick = (row, column) => {
            handleEdit(row, column.property);
        }
        const handleRefresh = () => {
            _getSForward();
            ElMessage.success(t('connom.refresh'))
        }
        const handleAdd = () => {
            state.loading = true;
            const row = { Id: 0, Name: '', RemotePort: 0, LocalEP: '127.0.0.1:80',Domain:'',Temp:'' };
            sforwardAddClient({machineid:sforward.value.machineid,data:row}).then(() => {
                state.loading = false;
                setTimeout(()=>{
                    _getSForward();
                },100)
            }).catch((err) => {
                state.loading = false;
                ElMessage.error(err);
            });
        }
        const handleEdit = (row, p) => {
            if(row.Started){
                ElMessage.error(t('sforward.stop'));
                return;
            }
            state.data.forEach(c => {
                c[`NameEditing`] = false;
                c[`RemotePortEditing`] = false;
                c[`LocalEPEditing`] = false;
                c[`DomainEditing`] = false;
                c[`TempEditing`] = false;
                c[`NodeIdEditing`] = false;
            })
            row[`${p}Editing`] = true;
            state.editing = true;
        }
        const handleEditBlur = (row, p) => {
            if(row.Started){
                ElMessage.error(t('sforward.stop'));
                return;
            }
            row[`${p}Editing`] = false;
            state.editing = false;

            try{row[p] = row[p].trim();}catch(w){}
            saveRow(row);
        }
        const handleDel = (id) => {
            planDom.value.remove(id,'start');
            planDom.value.remove(id,'stop');
            removeSForward({machineid:sforward.value.machineid,id:id})
            .then(() => {
                state.loading = false;
                _getSForward();
            }).catch((err) => {
                state.loading = false;
                ElMessage.error(err);
            });
        }
        const handleStartChange = (row) => {
            state.loading = true;
            const func = row.Started 
            ? sforwardStop({machineid:sforward.value.machineid,id:row.Id}) 
            : sforwardStart({machineid:sforward.value.machineid,id:row.Id});

            func.then(() => {
                state.loading = false;
                _getSForward();
            }).catch((err) => {
                state.loading = false;
                ElMessage.error(err);
            });
            
        }
        const saveRow = (row) => {
            if(!row.Temp) return;
            if(/^\d+$/.test(row.Temp)){
                row.RemotePort = parseInt(row.Temp);
            }else{
                row.Domain = row.Temp;
            }
            state.loading = true;
            sforwardAddClient({machineid:sforward.value.machineid,data:row}).then((res) => {
                state.loading = false;
                if(res == false){
                    ElMessage.error(t('common.operFail'));
                }
                _getSForward();
            }).catch((err) => {
                state.loading = false;
                ElMessage.error(err);
            });
        }


        const _sforwardSubscribe = ()=>{
            clearTimeout(state.timer2);
            sforwardSubscribe().then((res)=>{
                res = [{NodeId:'*',Name:'*',Host:'*',Domain:''}].concat(res);
                state.nodes = res;
                state.nodesNames = res.reduce((json,item)=>{ json[item.NodeId] = item.Name; return json; },{});
                state.nodesJson = res.reduce((json,item)=>{ json[item.NodeId] = item; return json; },{});
                state.timer2 = setTimeout(_sforwardSubscribe,1000);
            }).catch(()=>{
                state.timer2 = setTimeout(_sforwardSubscribe,1000);
            });
        }


        onMounted(()=>{
            _getSForward();
            _sforwardTestLocal();
            _sforwardSubscribe();
        });
        onUnmounted(()=>{
            clearTimeout(state.timer);
            clearTimeout(state.timer1);
            clearTimeout(state.timer2);
        })

        return {
            state,planDom,
            machineName:sforward.value.machineName, 
            machineId:sforward.value.machineid, 
            handleOnShowList, handleCellClick, handleRefresh, handleAdd, handleEdit, handleEditBlur, handleDel, handleStartChange
        }
    }
}
</script>
<style lang="stylus" scoped>

.head{padding-bottom:1rem}

.error{
    font-weight:bold;
    .el-icon{
        vertical-align:text-bottom
    }
}
.plan{
    .el-icon{
        vertical-align:middle;
        margin-right:0.4rem;
    }
}
</style>